using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlagInjector;

sealed class HistoryForm : Form
{
    private readonly DataGridView _grid = new();

    public HistoryForm(FlagEntry f)
    {
        Text            = $"History — {f.Name}";
        Size            = new Size(520, 370);
        StartPosition   = FormStartPosition.CenterParent;
        AutoScaleMode   = AutoScaleMode.Font;
        MinimumSize     = new Size(380, 260);

        _grid.Dock                     = DockStyle.Fill;
        _grid.ReadOnly                 = true;
        _grid.AllowUserToAddRows       = false;
        _grid.RowHeadersVisible        = false;
        _grid.EnableHeadersVisualStyles = false;
        _grid.AutoSizeColumnsMode      = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode            = DataGridViewSelectionMode.FullRowSelect;
        _grid.AccessibleName           = "Flag history";
        _grid.GridColor                = Theme.C.Border;

        var colTime = new DataGridViewTextBoxColumn { Name = "Time",  HeaderText = "Time",      SortMode = DataGridViewColumnSortMode.Automatic };
        var colOld  = new DataGridViewTextBoxColumn { Name = "Old",   HeaderText = "Old Value", SortMode = DataGridViewColumnSortMode.Automatic };
        var colNew  = new DataGridViewTextBoxColumn { Name = "New",   HeaderText = "New Value", SortMode = DataGridViewColumnSortMode.Automatic };
        _grid.Columns.AddRange(colTime, colOld, colNew);

        if (f.History.Count == 0)
        {
            _grid.Rows.Add("", "No history recorded", "");
        }
        else
        {
            for (int i = f.History.Count - 1; i >= 0; i--)
            {
                var h  = f.History[i];
                string ts = DateTime.TryParse(h.Timestamp, out var dt)
                    ? dt.ToLocalTime().ToString("g")
                    : h.Timestamp;
                int idx = _grid.Rows.Add(ts, h.OldValue, h.NewValue);
                _grid.Rows[idx].DefaultCellStyle.BackColor =
                    idx % 2 == 0 ? Theme.C.Bg : Theme.C.Row2;
            }
        }

        ApplyTheme();
        Controls.Add(_grid);

        Theme.Changed += ApplyTheme;
        FormClosed    += (_, _) => Theme.Changed -= ApplyTheme;
    }

    private void ApplyTheme()
    {
        BackColor                                   = Theme.C.Bg;
        ForeColor                                   = Theme.C.Fg;
        _grid.BackgroundColor                       = Theme.C.Bg;
        _grid.DefaultCellStyle.BackColor            = Theme.C.Bg;
        _grid.DefaultCellStyle.ForeColor            = Theme.C.Fg;
        _grid.DefaultCellStyle.SelectionBackColor   = Theme.C.Hover;
        _grid.DefaultCellStyle.SelectionForeColor   = Theme.C.Fg;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.C.Row2;
        _grid.ColumnHeadersDefaultCellStyle.BackColor   = Theme.C.Surface;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor   = Theme.C.Sub;
        _grid.GridColor                             = Theme.C.Border;
        Invalidate(true);
    }
}