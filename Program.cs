using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SfOAuth.Data;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddHttpClient("Salesforce", client =>
{
    client.BaseAddress = new Uri("https://irissystems-dev-ed.my.salesforce.com");
});

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>();
builder.Services.AddScoped<SalesforceAuthService>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
