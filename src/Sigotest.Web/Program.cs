using Microsoft.AspNetCore.Authentication.Negotiate;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var authentication = builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme);
if (!builder.Environment.IsEnvironment("Testing"))
{
    authentication.AddNegotiate();
}

builder.Services.AddAuthorization(options =>
{
    // By default, all incoming requests will be authorized according to the default policy.
    options.FallbackPolicy = options.DefaultPolicy;
});
builder.Services.AddRazorPages();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    application = "Sigotest",
    machine = Environment.MachineName,
    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    utc = DateTimeOffset.UtcNow
})).AllowAnonymous();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

public partial class Program;
