using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Runtime.InteropServices;

namespace Sigotest.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IConfiguration _configuration;

    public IndexModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string MachineName { get; private set; } = string.Empty;
    public string FrameworkDescription { get; private set; } = string.Empty;
    public DateTimeOffset ServerDateTime { get; private set; }
    public bool IsConnectionStringConfigured { get; private set; }

    public void OnGet()
    {
        MachineName = Environment.MachineName;
        FrameworkDescription = RuntimeInformation.FrameworkDescription;
        ServerDateTime = DateTimeOffset.Now;
        IsConnectionStringConfigured = !string.IsNullOrWhiteSpace(_configuration.GetConnectionString("SigotestDb"));
    }
}
