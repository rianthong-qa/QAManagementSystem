namespace ProMaxx2.Automation.AgentGui;

public sealed partial class CaptureForm
{
    private void EditSelected()
    {
        var index = _events.SelectedIndex;
        if (index < 0 || index >= _items.Count) { MessageBox.Show("Select a captured step first."); return; }
        var item = _items[index];
        var action = Prompt("Action", item.Action); if (action is null) return;
        var data = Prompt("TestData (sensitive values remain masked)", item.Sensitive ? "" : item.TestData ?? ""); if (data is null) return;
        var expected = Prompt("Expected Result", item.ExpectedResult); if (expected is null) return;
        _items[index] = item with { Action = action.Trim(), TestData = item.Sensitive ? null : data, ExpectedResult = expected.Trim() };
        _events.Items[index] = $"{item.StepNo}. {item.EventType} | {item.AutomationId ?? "Missing AutomationId"} | {action.Trim()}";
    }

    private static string? Prompt(string title, string value)
    {
        using var form = new Form { Text = title, Width = 560, Height = 170, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var box = new TextBox { Text = value, Dock = DockStyle.Top, Margin = new Padding(10) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 90 };
        form.Controls.Add(box); form.Controls.Add(cancel); form.Controls.Add(ok); form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? box.Text : null;
    }
}
