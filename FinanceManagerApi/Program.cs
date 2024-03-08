using FinanceManagerApi.DbContext;
using FinanceManagerApi.Models.Entity.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connstring = builder.Configuration.GetConnectionString("DefaultConnection");

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<FinanceDbContext>(options =>
{
	options.UseMySql(connstring, ServerVersion.AutoDetect(connstring));
});
builder.Services.AddIdentity<UserProfile, IdentityRole>()
	.AddEntityFrameworkStores<FinanceDbContext>();

/*customize password requirements*/
builder.Services.Configure<IdentityOptions>(options =>
{
	options.Password.RequireDigit = false;
	options.Password.RequiredLength = 6;
	options.Password.RequireNonAlphanumeric = false;
	options.Password.RequireUppercase = false;
	options.Password.RequireLowercase = false;
	options.Password.RequiredUniqueChars = 0;
});

builder.Services.AddCors(options =>
{
	options.AddPolicy("AllowSites", builder =>
	{
		builder.AllowAnyOrigin()
			.AllowAnyMethod()
			.AllowAnyHeader();
	});
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.UseCors("AllowSites");
app.Run();
