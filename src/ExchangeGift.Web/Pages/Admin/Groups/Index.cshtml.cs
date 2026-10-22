using ExchangeGift.Web.Data;
using ExchangeGift.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ExchangeGift.Web.Pages.Admin.Groups;

public class IndexModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<GroupRow> Rows { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        Rows = await GroupQueries.RowsAsync(db);
    }
}
