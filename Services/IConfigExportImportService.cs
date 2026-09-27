using System.Threading.Tasks;

namespace SplateClientDesktop.Services;

public interface IConfigExportImportService
{
    Task ExportAsync(string filePath, string passphrase);
    Task ImportAsync(string filePath, string passphrase);
}
