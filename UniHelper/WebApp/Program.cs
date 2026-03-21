namespace WebApp;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(
                builder =>
                {
                    builder.AllowAnyOrigin() 
                           .AllowAnyMethod()
                           .AllowAnyHeader();
                });
        });

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseCors();

        app.MapGet("/", () => Results.Redirect("/swagger"));
        app.MapGet("/visits", AnalyticsEndpoints.GetSiteVisits);
        app.MapGet("/chat-transitions", AnalyticsEndpoints.GetChatTransitions);
        app.MapGet("/stats", AnalyticsEndpoints.GetStats); 

        app.Urls.Add("http://0.0.0.0:5285");

        app.Run();
    }
}