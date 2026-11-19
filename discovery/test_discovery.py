"""Offline exercise using the ACT fixture compiled by PortalTests."""
import hashlib
import io
import sys
import zipfile
from pathlib import Path
from unittest.mock import patch

from discover import ROOT, discover

inspector = ROOT / "discovery/bin/Release/net8.0/Inspector.dll"
source = Path(sys.argv[1])
zip_name = "New-Package-1.0.0.zip"
manifest = "SHA256SUMS.txt"
archive = io.BytesIO()
with zipfile.ZipFile(archive, "w") as z:
    z.write(source, source.name)
files = {zip_name: archive.getvalue()}
files[manifest] = (hashlib.sha256(files[zip_name]).hexdigest() + "  " + zip_name).encode()
repo = dict(name="Fresh.Tool", owner={"login": "Roxyz0501"}, isPrivate=False, isArchived=False,
            isFork=False, description="Marker display", latestRelease=dict(id="r", tagName="v1.0.0",
            isDraft=False, isPrerelease=False, releaseAssets={"nodes": [
                {"id": n, "name": n, "size": len(data), "updatedAt": "now"} for n, data in files.items()]}))
calls = []


def fetch(url, limit):
    name = url.rsplit("/", 1)[1]
    calls.append(name)
    return files[name]


row = discover(repo, None, inspector, fetch)
assert row["Repo"] == "fresh.tool" and row["Dll"] == source.stem and row["Prefix"] == "New-Package"
assert calls == [manifest, zip_name]
calls.clear()
assert discover(repo, row, inspector, fetch) == row and calls == []
for change in (dict(isFork=True), dict(isPrivate=True), dict(isArchived=True),
               dict(name="inventory-ledger-act"), dict(owner={"login": "SomeoneElse"}),
               dict(latestRelease=None), dict(latestRelease=dict(repo["latestRelease"], isPrerelease=True))):
    assert discover(dict(repo, **change), None, inspector, fetch) is None
assert calls == []
with patch("discover.inspect_zip", return_value=None):
    assert discover(repo, None, inspector, fetch) is None
print("New release: two asset reads. Unchanged or excluded: zero asset reads.")
