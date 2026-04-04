using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FlagInjector;

sealed class DiffForm : Form
{
    private const string ColFlag     = "ColFlag";
    private const string ColCurrent  = "ColCurrent";
    private const string ColImported = "ColImported";
    private const string ColStatus   = "ColStatus";

    private readonly DataGridView _grid    = new();
    private readonly Label        _skipLbl = new();
    private readonly List<(string name, string value)> _toApply = new();

    public IReadOnlyList<(string name, string value)> ToApply => _toApply;

    private record RowData(string Name, string CurVal, string ImpVal, string Status);

    public DiffForm(List<FlagEntry> current, Dictionary<string, string> imported)
    {
        Text          = "Flag Comparison";
        Size          = new Size(720, 520);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize   = new Size(520, 320);
        AutoScaleMode = AutoScaleMode.Font;

        var curMap = current.ToDictionary(
            f => f.Name, f => f.Value, StringComparer.OrdinalIgnoreCase);

        var rows = current.Select(f => f.Name)
            .Union(imported.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(k =>
            {
                bool inCur = curMap.TryGetValue(k, out string? cv);
                bool inImp = imported.TryGetValue(k, out string? iv);
                string st  = inCur && inImp
                    ? (cv == iv ? "Same" : "Modified")
                    : inCur ? "Removed" : "New";
                return new RowData(k, cv ?? "", iv ?? "", st);
            })
            .ToList();

        _grid.Dock                     = DockStyle.Fill;
        _grid.ReadOnly                 = true;
        _grid.AllowUserToAddRows       = false;
        _grid.RowHeadersVisible        = false;
        _grid.EnableHeadersVisualStyles = false;
        _grid.SelectionMode            = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect              = true;
        _grid.AccessibleName           = "Flag diff";
        _grid.GridColor                = Theme.C.Border;

        var cFlag = new DataGridViewTextBoxColumn
            { Name = ColFlag,     HeaderText = "Flag",     SortMode = DataGridViewColumnSortMode.Automatic };
        var cCur  = new DataGridViewTextBoxColumn
            { Name = ColCurrent,  HeaderText = "Current",  SortMode = DataGridViewColumnSortMode.Automatic };
        var cImp  = new DataGridViewTextBoxColumn
            { Name = ColImported, HeaderText = "Imported", SortMode = DataGridViewColumnSortMode.Automatic };
        var cSt   = new DataGridViewTextBoxColumn
            { Name = ColStatus,   HeaderText = "Status",
              AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 90,
              SortMode = DataGridViewColumnSortMode.Automatic };

        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(cFlag, cCur, cImp, cSt);
        cSt.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        cSt.Width        = 90;

        foreach (var r in rows)
        {
            int idx = _grid.Rows.Add(r.Name, r.CurVal, r.ImpVal, r.Status);
            var row = _grid.Rows[idx];
            row.DefaultCellStyle.ForeColor = r.Status switch
            {
                "New"      => Theme.C.Green,
                "Modified" => Theme.C.Yellow,
                "Removed"  => Theme.C.Red,
                _          => Theme.C.Sub,
            };
            row.DefaultCellStyle.BackColor = idx % 2 == 0 ? Theme.C.Bg : Theme.C.Row2;
        }

        var toolbar = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            Height        = 44,
            Padding       = new Padding(8, 6, 8, 6),
            FlowDirection = FlowDirection.LeftToRight,
        };

        var btnApply = new GlowButton
        {
            ButtonText = "Apply Selected",
            Width      = 140,
            Height     = 30,
            BaseColor  = Theme.C.Accent,
            HoverColor = Color.FromArgb(
                Math.Min(255, Theme.C.Accent.R + 30),
                Math.Min(255, Theme.C.Accent.G + 20),
                Math.Min(255, Theme.C.Accent.B + 10)),
            FgColor    = Theme.C.Bg,
            GlowColor  = Theme.C.Accent,
            AccessibleName = "Apply selected differences",
        };
        btnApply.Clicked += OnApply;

        var btnAll  = new GlowButton { ButtonText = "Select All",  Width = 90, Height = 30,
            BaseColor = Theme.C.Surface, HoverColor = Theme.C.Hover,
            FgColor = Theme.C.Fg, GlowColor = Theme.C.Accent, AccessibleName = "Select all rows" };
        btnAll.Clicked += (_, _) => _grid.SelectAll();

        var btnNone = new GlowButton { ButtonText = "Select None", Width = 100, Height = 30,
            BaseColor = Theme.C.Surface, HoverColor = Theme.C.Hover,
            FgColor = Theme.C.Fg, GlowColor = Theme.C.Border, AccessibleName = "Clear selection" };
        btnNone.Clicked += (_, _) => _grid.ClearSelection();

        _skipLbl.AutoSize  = true;
        _skipLbl.TextAlign = ContentAlignment.MiddleLeft;
        _skipLbl.Font      = HpFont.Body(8.5f, FontStyle.Italic);
        _skipLbl.Padding   = new Padding(8, 6, 0, 0);

        var btnClose = new GlowButton { ButtonText = "Close", Width = 80, Height = 30,
            BaseColor = Theme.C.Surface, HoverColor = Theme.C.Hover,
            FgColor = Theme.C.Fg, GlowColor = Theme.C.Border, AccessibleName = "Close dialog" };
        btnClose.Clicked += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        toolbar.Controls.Add(btnApply);
        toolbar.Controls.Add(btnAll);
        toolbar.Controls.Add(btnNone);
        toolbar.Controls.Add(_skipLbl);
        toolbar.Controls.Add(btnClose);

        Controls.Add(_grid);
        Controls.Add(toolbar);

        ApplyTheme();
        Theme.Changed += ApplyTheme;
        FormClosed    += (_, _) => Theme.Changed -= ApplyTheme;
    }

    private void OnApply(object? s, EventArgs e)
    {
        _toApply.Clear();
        int skipped = 0;
        foreach (DataGridViewRow row in _grid.SelectedRows)
        {
            string st = row.Cells[ColStatus].Value?.ToString() ?? "";
            if (st is "New" or "Modified")
                _toApply.Add((
                    row.Cells[ColFlag].Value?.ToString()     ?? "",
                    row.Cells[ColImported].Value?.ToString() ?? ""));
            else if (st == "Removed")
                skipped++;
        }
        _skipLbl.Text      = skipped > 0 ? $"{skipped} removed skipped" : "";
        _skipLbl.ForeColor = Theme.C.Sub;
        if (_toApply.Count > 0) { DialogResult = DialogResult.OK; Close(); }
    }

    private void ApplyTheme()
    {
        BackColor                                     = Theme.C.Bg;
        ForeColor                                     = Theme.C.Fg;
        _grid.BackgroundColor                         = Theme.C.Bg;
        _grid.DefaultCellStyle.BackColor              = Theme.C.Bg;
        _grid.DefaultCellStyle.ForeColor              = Theme.C.Fg;
        _grid.DefaultCellStyle.SelectionBackColor     = Theme.C.Hover;
        _grid.DefaultCellStyle.SelectionForeColor     = Theme.C.Fg;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.C.Row2;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.C.Surface;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.C.Sub;
        _grid.GridColor                               = Theme.C.Border;
        foreach (Control ctrl in Controls)
            if (ctrl is FlowLayoutPanel fp) fp.BackColor = Theme.C.Surface;
        _skipLbl.ForeColor = Theme.C.Sub;
        Invalidate(true);
    }
}