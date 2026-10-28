using System.Text.Json;
using System.Net.Http.Json;

namespace ProMaxx2.Automation.AgentGui;

public sealed partial class CaptureForm
{
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var button = new Button { Text = "Local Preview Detail", Dock = DockStyle.Bottom, Height = 34 };
        button.Click += (_, _) => ShowPreview(JsonSerializer.SerializeToElement(new { items = _items.Select(x => new { stepNo = x.StepNo, objectCode = x.ObjectCode, automationId = x.AutomationId, status = string.IsNullOrWhiteSpace(x.AutomationId) ? "Missing AutomationId" : "New" }) }));
        Controls.Add(button);
        var serverButton = new Button { Text = "Preview from QA Hub", Dock = DockStyle.Bottom, Height = 34 };
        serverButton.Click += async (_, _) => await SendServerPreviewAsync();
        Controls.Add(serverButton);
    }

    private async Task SendServerPreviewAsync()
    {
        try
        {
            if (_project.SelectedItem is not JsonElement p || _module.SelectedItem is not JsonElement m || _testCase.SelectedItem is not JsonElement t) throw new InvalidOperationException("เลือก Project, Module และ Test Case ก่อน");
            var request = new { projectId = p.GetProperty("projectId").GetGuid(), moduleId = m.GetProperty("moduleId").GetGuid(), testCaseId = t.GetProperty("testCaseId").GetGuid(), applicationCode = _app.Text, sourceMachine = Environment.MachineName, applicationVersion = Application.ProductVersion, items = _items };
            using var client = Client(); var response = await client.PostAsJsonAsync($"{_url.Text.TrimEnd('/')}/automation/capture/sessions", request); response.EnsureSuccessStatusCode();
            var session = await response.Content.ReadFromJsonAsync<JsonElement>(); ShowPreview(session);
        }
        catch (Exception ex) { MessageBox.Show($"Preview QA Hub ไม่สำเร็จ: {ex.Message}"); }
    }

    private static void ShowPreview(JsonElement session)
    {
        using var form = new Form { Text = "Capture Preview", Width = 760, Height = 460, StartPosition = FormStartPosition.CenterParent };
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Step", DataPropertyName = "StepNo", Width = 55 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Object", DataPropertyName = "ObjectCode", Width = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "AutomationId", DataPropertyName = "AutomationId", Width = 210 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", DataPropertyName = "Status", Width = 150 });
        var rows = new List<PreviewRow>();
        if (session.TryGetProperty("items", out var items)) foreach (var item in items.EnumerateArray()) rows.Add(new(item.GetProperty("stepNo").GetInt32(), item.GetProperty("objectCode").GetString() ?? "", item.TryGetProperty("automationId", out var id) ? id.GetString() ?? "" : "", item.GetProperty("status").GetString() ?? ""));
        grid.DataSource = rows; form.Controls.Add(grid); form.ShowDialog();
    }
    private sealed record PreviewRow(int StepNo, string ObjectCode, string AutomationId, string Status);
}
