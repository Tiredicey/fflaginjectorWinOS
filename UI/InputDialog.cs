using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlagInjector;

sealed class InputDialog : Form
{
    public string Value => _tb.Text.Trim();

    private readonly TextBox _tb  = new();
    private readonly Button  _ok  = new();

    public InputDialog(string title, string prompt, string defaultVal = "", int maxLength = 512)
    {
        Text            = title;
        AutoScaleMode   = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize      = new Size(390, 148);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;

        ApplyTheme();

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 3,
            Padding     = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lbl = new Label
        {
            Text      = prompt,
            Dock      = DockStyle.Fill,
            AutoSize  = true,
            Font      = HpFont.Body(9.5f),
            ForeColor = Theme.C.Fg,
            Padding   = new Padding(0, 0, 0, 4),
        };
        lbl.AccessibleName = prompt;

        _tb.Dock         = DockStyle.Fill;
        _tb.Text         = defaultVal;
        _tb.MaxLength    = maxLength;
        _tb.BackColor    = Theme.C.Surface;
        _tb.ForeColor    = Theme.C.Fg;
        _tb.BorderStyle  = BorderStyle.FixedSingle;
        _tb.Font         = HpFont.Body(9.5f);
        _tb.AccessibleName = prompt;
        _tb.TextChanged += (_, _) => _ok.Enabled = Value.Length > 0;

        var btnRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize      = true,
            Padding       = new Padding(0, 8, 0, 0),
        };

        var cancel = new Button
        {
            Text         = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width        = 80,
            Height       = 28,
            FlatStyle    = FlatStyle.Flat,
            BackColor    = Theme.C.Surface,
            ForeColor    = Theme.C.Fg,
            Font         = HpFont.Body(9f),
            AccessibleName = "Cancel",
        };
        cancel.FlatAppearance.BorderColor = Theme.C.Border;

        _ok.Text          = "OK";
        _ok.DialogResult  = DialogResult.OK;
        _ok.Width         = 80;
        _ok.Height        = 28;
        _ok.FlatStyle     = FlatStyle.Flat;
        _ok.BackColor     = Theme.C.Accent;
        _ok.ForeColor     = Theme.C.Bg;
        _ok.Font          = HpFont.Body(9f, FontStyle.Bold);
        _ok.Enabled       = defaultVal.Trim().Length > 0;
        _ok.FlatAppearance.BorderSize = 0;
        _ok.AccessibleName = "OK";

        AcceptButton = _ok;
        CancelButton = cancel;

        btnRow.Controls.Add(cancel);
        btnRow.Controls.Add(_ok);

        layout.Controls.Add(lbl,    0, 0);
        layout.Controls.Add(_tb,    0, 1);
        layout.Controls.Add(btnRow, 0, 2);

        layout.BackColor = Theme.C.Bg;
        Controls.Add(layout);

        Theme.Changed += ApplyTheme;
        FormClosed    += (_, _) => Theme.Changed -= ApplyTheme;
    }

    private void ApplyTheme()
    {
        BackColor    = Theme.C.Bg;
        ForeColor    = Theme.C.Fg;
        _tb.BackColor = Theme.C.Surface;
        _tb.ForeColor = Theme.C.Fg;
        Invalidate(true);
    }
}