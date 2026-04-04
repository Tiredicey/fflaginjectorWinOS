using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FlagInjector;

sealed class BackgroundPickerDialog : Form
{
    public string   SelectedPreset { get; private set; }
    public string   ImagePath      { get; private set; }
    public float    Opacity        { get; private set; }

    private readonly ListBox   _presetList = new();
    private readonly TrackBar  _opacityBar = new();
    private readonly Label     _opacityLbl = new();
    private readonly Label     _pathLbl    = new();
    private string             _customPath = "";

    private static readonly string[] _presets = { "Stars", "Mist", "Runes", "None", "Custom" };

    public BackgroundPickerDialog()
    {
        var s = AppSettings.Instance;
        SelectedPreset = s.BackgroundPreset;
        ImagePath      = s.BackgroundImagePath;
        Opacity        = (float)s.BackgroundOpacity;

        Text            = "Background & Atmosphere";
        Size            = new Size(400, 320);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        AutoScaleMode   = AutoScaleMode.Font;

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            Padding     = new Padding(14),
        };

        var presetLabel = new Label
        {
            Text      = "Atmosphere Preset",
            Font      = HpFont.Heading(10f),
            ForeColor = Theme.C.Accent,
            AutoSize  = true,
            Padding   = new Padding(0, 0, 0, 4),
        };

        _presetList.Dock           = DockStyle.Fill;
        _presetList.Height         = 100;
        _presetList.BackColor      = Theme.C.Surface;
        _presetList.ForeColor      = Theme.C.Fg;
        _presetList.BorderStyle    = BorderStyle.FixedSingle;
        _presetList.Font           = HpFont.Body(9.5f);
        _presetList.AccessibleName = "Background preset";
        foreach (var p in _presets) _presetList.Items.Add(p);
        int selIdx = Array.IndexOf(_presets, SelectedPreset);
        _presetList.SelectedIndex = selIdx >= 0 ? selIdx : 0;
        _presetList.SelectedIndexChanged += (_, _) =>
        {
            SelectedPreset = _presetList.SelectedItem?.ToString() ?? "Stars";
            if (SelectedPreset == "Custom") BrowseImage();
        };

        _pathLbl.Text      = Path.GetFileName(ImagePath) is { Length: > 0 } n ? n : "No image selected";
        _pathLbl.Font      = HpFont.Body(8.5f, FontStyle.Italic);
        _pathLbl.ForeColor = Theme.C.Sub;
        _pathLbl.AutoSize  = true;
        _pathLbl.Padding   = new Padding(0, 2, 0, 2);

        var opacityLabel = new Label
        {
            Text      = "Overlay Opacity",
            Font      = HpFont.Body(9.5f),
            ForeColor = Theme.C.Fg,
            AutoSize  = true,
            Padding   = new Padding(0, 6, 0, 2),
        };

        _opacityBar.Minimum  = 0;
        _opacityBar.Maximum  = 100;
        _opacityBar.Value    = (int)(Opacity * 100f);
        _opacityBar.Dock     = DockStyle.Fill;
        _opacityBar.TickFrequency = 10;
        _opacityBar.AccessibleName = "Opacity";
        _opacityBar.ValueChanged += (_, _) =>
        {
            Opacity     = _opacityBar.Value / 100f;
            _opacityLbl.Text = $"{_opacityBar.Value}%";
        };

        _opacityLbl.Text      = $"{_opacityBar.Value}%";
        _opacityLbl.Font      = HpFont.Body(9f);
        _opacityLbl.ForeColor = Theme.C.Accent;
        _opacityLbl.AutoSize  = true;

        var btnRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize      = true,
            Padding       = new Padding(0, 10, 0, 0),
        };

        var btnCancel = new Button
        {
            Text = "Cancel", DialogResult = DialogResult.Cancel,
            Width = 80, Height = 28, FlatStyle = FlatStyle.Flat,
            BackColor = Theme.C.Surface, ForeColor = Theme.C.Fg,
            Font = HpFont.Body(9f),
        };
        btnCancel.FlatAppearance.BorderColor = Theme.C.Border;

        var btnOk = new Button
        {
            Text = "Apply", DialogResult = DialogResult.OK,
            Width = 80, Height = 28, FlatStyle = FlatStyle.Flat,
            BackColor = Theme.C.Accent, ForeColor = Theme.C.Bg,
            Font = HpFont.Body(9f, FontStyle.Bold),
        };
        btnOk.FlatAppearance.BorderSize = 0;
        btnOk.Click += (_, _) => SaveAndClose();

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        btnRow.Controls.Add(btnCancel);
        btnRow.Controls.Add(btnOk);

        layout.Controls.Add(presetLabel);
        layout.Controls.Add(_presetList);
        layout.Controls.Add(_pathLbl);
        layout.Controls.Add(opacityLabel);
        layout.Controls.Add(_opacityBar);
        layout.Controls.Add(_opacityLbl);
        layout.Controls.Add(btnRow);

        BackColor        = Theme.C.Bg;
        ForeColor        = Theme.C.Fg;
        layout.BackColor = Theme.C.Bg;
        Controls.Add(layout);
    }

    private void BrowseImage()
    {
        using var ofd = new OpenFileDialog
        {
            Title  = "Select Background Image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All|*.*",
        };
        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            _customPath      = ofd.FileName;
            ImagePath        = _customPath;
            _pathLbl.Text    = Path.GetFileName(_customPath);
        }
    }

    private void SaveAndClose()
    {
        var s                = AppSettings.Instance;
        s.BackgroundPreset   = SelectedPreset;
        s.BackgroundImagePath = ImagePath;
        s.BackgroundOpacity  = Opacity;
        s.Save();
        DialogResult = DialogResult.OK;
        Close();
    }
}