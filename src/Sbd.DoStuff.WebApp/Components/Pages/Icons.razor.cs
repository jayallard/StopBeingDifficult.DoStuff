using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Sbd.DoStuff.Domain.Icons;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class Icons
{
    private IReadOnlyList<IconFile> _icons = [];
    private IReadOnlyList<IconReference> _references = [];
    private List<IconReference> _missing = [];
    private string? _error;
    private string _newName = "";
    private string? _confirmDelete;
    private IBrowserFile? _newFile;

    private const long MaxIconBytes = 256 * 1024;

    [Inject] private TaskIconCatalog Catalog { get; set; } = null!;

    [Inject] private IconUsageScanner Scanner { get; set; } = null!;

    protected override void OnInitialized() => Refresh();

    private void Refresh()
    {
        _icons = Catalog.GetAll();
        _references = Scanner.Scan();
        _missing = _references.Where(r => !Catalog.TryFindFile(r.Name, out _)).ToList();
    }

    private IconReference? UsagesOf(string name)
        => _references.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void OnFileChosen(InputFileChangeEventArgs e) => _newFile = e.File;

    private async Task AddAsync()
    {
        try
        {
            if (_newFile is null)
            {
                Catalog.Create(_newName);
            }
            else
            {
                await using var stream = _newFile.OpenReadStream(MaxIconBytes);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                Catalog.Add(_newName, Path.GetExtension(_newFile.Name), buffer.ToArray());
            }

            _error = null;
            _newName = "";
            _newFile = null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _error = ex.Message;
        }

        Refresh();
    }

    private string DeleteWarning(string name)
    {
        var count = UsagesOf(name)?.Usages.Count ?? 0;
        return count == 0
            ? "Delete this icon?"
            : $"Used {count} time{(count == 1 ? "" : "s")}; those will show the default icon.";
    }

    private void Delete(string name)
    {
        try
        {
            Catalog.Delete(name);
            _error = null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _error = ex.Message;
        }

        _confirmDelete = null;
        Refresh();
    }

    private void Create(string name)
    {
        try
        {
            Catalog.Create(name);
            _error = null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _error = ex.Message;
        }

        Refresh();
    }
}
