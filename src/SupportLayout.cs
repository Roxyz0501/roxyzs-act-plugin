using System;
using System.Drawing;
using System.Windows.Forms;
namespace RoxyzsActPlugin
{
    public sealed partial class Portal
    {
        private Control BuildSupportLayout()
        {
            var supportTitle=new Label { AutoSize=true, Text=L("Support Roxyz0501's development","Roxyz0501の開発を支援","支持Roxyz0501的开发","Roxyz0501의 개발 후원") };
            var supportDescription=new Label { AutoSize=true, Text=L("You can optionally support the development and continued improvement of Roxyzs ACT Plugin through Ko-fi.","Roxyzs ACT Pluginの開発と継続的な改善を、Ko-fiから任意で支援できます。","你可以通过Ko-fi自愿支持Roxyzs ACT Plugin的开发与持续改进。","Ko-fi를 통해 Roxyzs ACT Plugin 개발과 지속적인 개선을 선택적으로 후원할 수 있습니다.") };
            var supportNoDifference=new Label { AutoSize=true, Text=L("Support is entirely optional. Every feature remains available with no functional difference if you do not contribute.","支援は完全に任意です。支援しなくても全機能を利用でき、機能差はありません。","支持完全自愿。即使不支持也能使用全部功能，不存在功能差异。","후원은 완전히 선택 사항입니다. 후원하지 않아도 모든 기능을 동일하게 사용할 수 있습니다.") };
            var supportUrl=new Label { AutoSize=true, Text="https://ko-fi.com/roxyz0501" };
            var supportSafety=new Label { AutoSize=true, Text=L("The external site opens in your default browser only when you click this button.","このボタンをクリックした場合のみ、既定のブラウザで外部サイトを開きます。","仅在你点击此按钮时，才会使用默认浏览器打开外部网站。","이 버튼을 클릭한 경우에만 기본 브라우저로 외부 사이트를 엽니다.") };
            var supportStatus=new Label { AutoSize=true, Text="" };
            var supportButton=new Button { Text=L("Support Roxyz0501 on Ko-fi","Ko-fiでRoxyz0501を支援する","在Ko-fi支持Roxyz0501","Ko-fi에서 Roxyz0501 후원") };
            supportButton.Click+=(s,e)=>Try(()=>Open("https://ko-fi.com/roxyz0501"));
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 400,
                ColumnCount = 1,
                RowCount = 8,
                Padding = new Padding(28, 24, 28, 20),
                BackColor = Color.White
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            supportTitle.AutoSize = false;
            supportTitle.Dock = DockStyle.Fill;
            supportTitle.Font = new Font("Yu Gothic UI Semibold", 17f, FontStyle.Bold);
            supportTitle.ForeColor = Color.FromArgb(166, 83, 15);
            supportTitle.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(supportTitle, 0, 0);

            var divider = new Panel
            {
                Dock = DockStyle.Top,
                Height = 2,
                BackColor = Color.FromArgb(218, 153, 50),
                Margin = new Padding(0, 3, 0, 7)
            };
            layout.Controls.Add(divider, 0, 1);

            supportDescription.AutoSize = false;
            supportDescription.Dock = DockStyle.Fill;
            supportDescription.Font = new Font("Yu Gothic UI", 10f);
            supportDescription.ForeColor = Color.FromArgb(48, 61, 80);
            layout.Controls.Add(supportDescription, 0, 2);

            supportNoDifference.AutoSize = false;
            supportNoDifference.Dock = DockStyle.Fill;
            supportNoDifference.Font = new Font("Yu Gothic UI Semibold", 9.5f, FontStyle.Bold);
            supportNoDifference.ForeColor = Color.FromArgb(125, 72, 20);
            layout.Controls.Add(supportNoDifference, 0, 3);

            supportButton.Width = 360;
            supportButton.Height = 50;
            supportButton.Anchor = AnchorStyles.Left;
            supportButton.FlatStyle = FlatStyle.Flat;
            supportButton.FlatAppearance.BorderSize = 1;
            supportButton.FlatAppearance.BorderColor = Color.FromArgb(164, 79, 12);
            supportButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(224, 126, 31);
            supportButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(174, 83, 13);
            supportButton.BackColor = Color.FromArgb(202, 101, 20);
            supportButton.ForeColor = Color.White;
            supportButton.Font = new Font("Yu Gothic UI Semibold", 11f, FontStyle.Bold);
            supportButton.Cursor = Cursors.Hand;
            supportButton.Margin = new Padding(0, 8, 0, 8);
            layout.Controls.Add(supportButton, 0, 4);

            supportUrl.ForeColor = Color.FromArgb(140, 91, 38);
            supportUrl.Anchor = AnchorStyles.Left;
            layout.Controls.Add(supportUrl, 0, 5);

            supportSafety.AutoSize = false;
            supportSafety.Dock = DockStyle.Fill;
            supportSafety.ForeColor = Color.FromArgb(73, 96, 128);
            layout.Controls.Add(supportSafety, 0, 6);

            supportStatus.AutoSize = false;
            supportStatus.Dock = DockStyle.Fill;
            supportStatus.Font = new Font("Yu Gothic UI Semibold", 9f, FontStyle.Bold);
            layout.Controls.Add(supportStatus, 0, 7);
            return layout;
        }

    }
}
