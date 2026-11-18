WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

WebApplication app = builder.Build();

app.MapGet("/", () => "Kairo sample is running.");

app.Run();
