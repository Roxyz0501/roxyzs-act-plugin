"""Build the portal catalog from stable public Releases owned by Roxyz0501."""
import hashlib
import io
import json
import re
import subprocess
import sys
import tempfile
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OWNER = "Roxyz0501"
BLOCKED = {"inventory-ledger-act", "mitigation-flytext-act", "screenshotmetadata", "roxyzs-act-plugin"}
BLOCKED_DLLS = {"inventoryledger", "mitigationflytext", "screenshotmetadata", "roxyzsactplugin"}
IDENTIFIER = r"[A-Za-z][A-Za-z0-9_.-]{0,63}"
PREFIX = r"[A-Za-z][A-Za-z0-9_.-]{0,79}"
VERSION = r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)"
LIMIT = 32 * 1024 * 1024
ASSETS = "nodes { id name size updatedAt } pageInfo { hasNextPage endCursor }"
QUERY = """query($cursor:String) {
  user(login:"Roxyz0501") {
    repositories(first:40,after:$cursor,privacy:PUBLIC,ownerAffiliations:[OWNER]) {
      pageInfo { hasNextPage endCursor }
      nodes { name owner { login } isPrivate isArchived isFork description
        latestRelease { id tagName isDraft isPrerelease releaseAssets(first:20) { ASSETS } }
      }
    }
  }
}""".replace("ASSETS", ASSETS)


def graphql(query, **variables):
    command = ["gh", "api", "graphql", "-f", "query=" + query]
    for key, value in variables.items():
        if value is not None:
            command.extend(["-f", key + "=" + value])
    return json.loads(subprocess.check_output(command))["data"]


def repositories():
    cursor = None
    while True:
        page = graphql(QUERY, cursor=cursor)["user"]["repositories"]
        for repo in page["nodes"]:
            release = repo["latestRelease"]
            if release and eligible(repo):
                assets = release["releaseAssets"]
                more = assets["pageInfo"]
                while more["hasNextPage"]:
                    query = ("query($id:ID!,$cursor:String!){node(id:$id){... on Release {"
                             "releaseAssets(first:50,after:$cursor){"+ASSETS+"}}}}")
                    part = graphql(query, id=release["id"], cursor=more["endCursor"])["node"]["releaseAssets"]
                    assets["nodes"].extend(part["nodes"])
                    more = part["pageInfo"]
            yield repo
        if not page["pageInfo"]["hasNextPage"]:
            return
        cursor = page["pageInfo"]["endCursor"]


def eligible(repo):
    slug = repo["name"].lower()
    return (repo["owner"]["login"].lower() == OWNER.lower() and not repo["isPrivate"]
            and not repo["isArchived"] and not repo["isFork"] and slug not in BLOCKED
            and not slug.endswith(".") and not reserved(slug)
            and re.fullmatch(r"[a-z0-9][a-z0-9_.-]{0,79}", slug))


def reserved(name):
    return bool(re.match(r"(?i)^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)", name))


def download(url, limit):
    request = urllib.request.Request(url, headers={"User-Agent": "RoxyzsActPlugin-catalog"})
    with urllib.request.urlopen(request, timeout=45) as response:
        data = response.read(limit + 1)
    if len(data) > limit:
        raise ValueError("oversized release asset")
    return data


def inspect_zip(data, version, inspector):
    with zipfile.ZipFile(io.BytesIO(data)) as archive, tempfile.TemporaryDirectory() as temp:
        if len(archive.infolist()) > 64:
            return None
        found = []
        for entry in archive.infolist():
            match = re.fullmatch("(" + IDENTIFIER + r")\.dll", entry.filename)
            if not match or entry.file_size > LIMIT:
                continue
            dll = match[1]
            if dll.lower() in BLOCKED_DLLS or reserved(dll):
                continue
            path = Path(temp) / "candidate.bin"
            path.write_bytes(archive.read(entry))
            code = subprocess.run(["dotnet", str(inspector), str(path), dll, version]).returncode
            if code == 0:
                found.append(dll)
            elif code != 1:
                raise RuntimeError("metadata inspector failed")
        return found[0] if len(found) == 1 else None


def discover(repo, old, inspector, fetch=download):
    if not eligible(repo):
        return None
    release = repo["latestRelease"]
    if not release or release["isDraft"] or release["isPrerelease"]:
        return None
    tag = release["tagName"]
    if not re.fullmatch("v?" + VERSION, tag):
        return None
    version = tag.lstrip("v")
    assets = {asset["name"]: asset for asset in release["releaseAssets"]["nodes"]}
    for name in sorted(assets):
        match = re.fullmatch("(" + PREFIX + ")-v?" + re.escape(version) + r"\.zip", name)
        if not match or not 0 < assets[name]["size"] <= LIMIT:
            continue
        manifest = next((m for m in (name + ".sha256", name[:-4] + ".sha256", "SHA256SUMS.txt")
                         if m in assets and 0 < assets[m]["size"] <= 16384), None)
        if not manifest:
            continue
        signature = hashlib.sha256(json.dumps([release["id"], assets[name], assets[manifest]],
                                              sort_keys=True).encode()).hexdigest()
        slug = repo["name"].lower()
        if old and old.get("Discovery") == signature:
            dll = old["Dll"]
        else:
            base = f"https://github.com/{OWNER}/{slug}/releases/download/{tag}/"
            sums = fetch(base + manifest, 16384).decode("utf-8-sig")
            digest = re.search(r"(?m)^([a-fA-F0-9]{64})[ \t]+\*?" + re.escape(name) + r"[ \t]*\r?$", sums)
            if not digest:
                continue
            data = fetch(base + name, LIMIT)
            if hashlib.sha256(data).hexdigest() != digest[1].lower():
                raise ValueError("SHA-256 mismatch: " + slug)
            dll = inspect_zip(data, version, inspector)
            if not dll:
                continue
        row = dict(old or {})
        if not old or old.get("AutoDescription"):
            description = (repo.get("description") or f"{dll} · ACT plugin").strip()[:512]
            row.update(Name=dll, Description=[description] * 4, AutoDescription=True)
        row.update(Repo=slug, Dll=dll, Prefix=match[1], Discovery=signature)
        return row
    return None


def main():
    catalog = ROOT / "catalog.json"
    previous = json.loads(catalog.read_text(encoding="utf-8"))
    old = {row["Repo"]: row for row in previous}
    rows = []
    for repo in repositories():
        row = discover(repo, old.get(repo["name"].lower()), Path(sys.argv[1]).resolve())
        if row:
            rows.append(row)
    rows.sort(key=lambda row: row["Name"].casefold())
    if len({row["Dll"].casefold() for row in rows}) != len(rows):
        raise ValueError("ambiguous plugin assembly names")
    output = json.dumps(rows, ensure_ascii=False, indent=2) + "\n"
    if len(rows) > 512 or len(output.encode()) > 512 * 1024:
        raise ValueError("catalog exceeds portal limits")
    if rows != previous:
        catalog.write_bytes(output.encode())
    print(f"Verified {len(rows)} public ACT plugins.")


if __name__ == "__main__":
    main()
