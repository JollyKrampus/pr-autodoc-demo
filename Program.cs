var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/hello", () => Results.Ok(new { message = "Hello world v1" }));
app.Run();
