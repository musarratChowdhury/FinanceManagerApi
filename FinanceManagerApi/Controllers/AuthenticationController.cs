using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using FinanceManagerApi.DbContext;
using FinanceManagerApi.Models.Entity.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace FinanceManagerApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthenticationController : ControllerBase
    {
        private readonly UserManager<UserProfile> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly FinanceDbContext _dbContext;
        private readonly IConfiguration _configuration;

        public AuthenticationController(UserManager<UserProfile> userManager, RoleManager<IdentityRole> roleManager, FinanceDbContext dbContext, IConfiguration configuration)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _dbContext = dbContext;
            _configuration = configuration;
        }
        
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterVM model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest("Please, provide all the required fields");
            }
            
            var userExists = await _userManager.FindByNameAsync(model.UserName);
            if (userExists != null)
                return BadRequest($"User {model.Email} already exists!");

            var user = new UserProfile()
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                Email = model.Email,
                SecurityStamp = Guid.NewGuid().ToString(),
                UserName = model.UserName
            };
            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
                return BadRequest("User creation failed! Please check user details and try again.");

            return Ok("User created successfully!");
        }
        
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginVM model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest("Please, provide all required fields");
            }
            
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user != null && await _userManager.CheckPasswordAsync(user, model.Password))
            {
                // var userRoles = await _userManager.GetRolesAsync(user);
                var response = await GenerateJWTTokenAsync(user);
                
                return Ok(response);
            }
            return Unauthorized();
        }

        private async Task<AuthResultVM> GenerateJWTTokenAsync(UserProfile user)
        {
            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            // foreach (var userRole in userRoles)
            // {
            //     authClaims.Add(new Claim(ClaimTypes.Role, userRole));
            // }

            var authSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_configuration["JWT:Secret"]));

            var token = new JwtSecurityToken(
                issuer: _configuration["JWT:Issuer"],
                audience: _configuration["JWT:Audience"],
                expires: DateTime.Now.AddHours(3),
                claims: authClaims,
                signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
            );

            var jwtToken = new JwtSecurityTokenHandler().WriteToken(token);

            var response = new AuthResultVM()
            {
                Token = jwtToken,
                ExpiresAt = token.ValidTo
            };
            return response;
        }
    }
}
