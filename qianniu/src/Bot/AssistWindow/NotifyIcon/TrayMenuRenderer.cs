using System.Drawing;
using System.Windows.Forms;

namespace Bot.AssistWindow.NotifyIcon
{
    /// <summary>
    /// 托盘小图标右键菜单统一用的字体，主菜单和"帮助"子菜单都要引用同一份，
    /// 否则子菜单的 Font 不会跟着父级 ContextMenuStrip 走。
    /// </summary>
    public static class TrayMenuStyle
    {
        public static readonly Font MenuFont = new Font("Microsoft YaHei UI", 9.5f);
    }

    /// <summary>
    /// 托盘菜单配色，替换 WinForms 默认的经典蓝色渐变高亮，跟主界面的浅色调风格靠近。
    /// </summary>
    public class TrayMenuColorTable : ProfessionalColorTable
    {
        public static readonly Color Accent = Color.FromArgb(0xEA, 0xF3, 0xFF);
        public static readonly Color AccentBorder = Color.FromArgb(0x40, 0x9E, 0xFF);
        public static readonly Color Border = Color.FromArgb(0xE4, 0xE7, 0xED);
        public static readonly Color Separator = Color.FromArgb(0xEB, 0xEE, 0xF3);

        public override Color MenuItemSelected { get { return Accent; } }
        public override Color MenuItemSelectedGradientBegin { get { return Accent; } }
        public override Color MenuItemSelectedGradientEnd { get { return Accent; } }
        public override Color MenuItemBorder { get { return AccentBorder; } }
        public override Color MenuBorder { get { return Border; } }
        public override Color ToolStripDropDownBackground { get { return Color.White; } }
        public override Color ImageMarginGradientBegin { get { return Color.White; } }
        public override Color ImageMarginGradientMiddle { get { return Color.White; } }
        public override Color ImageMarginGradientEnd { get { return Color.White; } }
        public override Color SeparatorDark { get { return Separator; } }
        public override Color SeparatorLight { get { return Color.White; } }
    }

    public class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer() : base(new TrayMenuColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item as ToolStripMenuItem;
            if (item != null && item.Enabled && (item.Selected || item.Pressed))
            {
                var bounds = new Rectangle(Point.Empty, e.Item.Size);
                using (var brush = new SolidBrush(TrayMenuColorTable.Accent))
                {
                    e.Graphics.FillRectangle(brush, bounds);
                }
                using (var pen = new Pen(TrayMenuColorTable.AccentBorder))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
                }
                return;
            }
            base.OnRenderMenuItemBackground(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            var y = bounds.Height / 2;
            using (var pen = new Pen(TrayMenuColorTable.Separator))
            {
                e.Graphics.DrawLine(pen, bounds.Left + 4, y, bounds.Right - 4, y);
            }
        }
    }
}
