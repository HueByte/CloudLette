using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common.ApiResponse;
using Common.lib.ApiResponse;
using Core.DTOModels;
using Core.Entities;
using Core.ServiceInterfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Core.RepositoriesInterfaces;
using Core.Models;

namespace API.Authentication
{
    public class UserService : IUserService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IJwtAuthentication _jwtAuthentication;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly IEmailConfirmationTokenRepository _emailRepo;

        public UserService(UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager, IJwtAuthentication jwtAuthentication,
        IEmailSender emailSender, IConfiguration configuration, IEmailConfirmationTokenRepository emailRepo, SignInManager<ApplicationUser> signInManager)
        {
            _emailRepo = emailRepo;
            _configuration = configuration;
            _emailSender = emailSender;
            _jwtAuthentication = jwtAuthentication;
            _roleManager = roleManager;
            _userManager = userManager;
            _signInManager = signInManager;

        }



        public async Task<LoginResponse> LoginUserAsync(LoginDTO loginModel)
        {
            try
            {
                // Find user by mail
                var user = await _userManager.FindByEmailAsync(loginModel.Email);
                // handle user not found 
                if (user == null) return new LoginResponse() { Errors = new List<string>() { "User not found" } };

                // Login via password
                var result = await _signInManager.CheckPasswordSignInAsync(user, loginModel.Password, false);
                // handle wrong password
                if (!result.Succeeded) return new LoginResponse() { Errors = new List<string>() { "Either e-mail or password is incorrect" } };
                // handle unverified email if user provided correct password and email
                if (!(await _userManager.IsEmailConfirmedAsync(user))) return new LoginResponse() { Errors = new List<string>() { "Verify your e-mail" } };

                //Generate token and return Service Response with Token in LoginResponse
                return new LoginResponse
                {
                    isSuccess = true,
                    token = _jwtAuthentication.GenerateJsonWebToken(user),
                    UserName = user.UserName,
                    avatar_url = user.Avatar_Url,
                    exp = user.exp,
                    Email = user.Email,
                    tokenType = "Bearer",
                    expiresDate = DateTime.Now.AddDays(30)
                };
            }
            catch (Exception e)
            {
                return new LoginResponse() { Errors = new List<string>() { e.ToString() } };
            }
        }


        public async Task<RegisterResponse> RegisterUserAsync(RegisterDTO registermodel)
        {
            if (registermodel == null)
                return new RegisterResponse() { Errors = new List<string>() { "EmptyForm" } };
            var user = new ApplicationUser()
            {
                Id = Guid.NewGuid().ToString(),
                UserName = registermodel.Username,
                Email = registermodel.Email,
                Roles = new List<string>() { "user" }
            };

            var result = await _userManager.CreateAsync(user, registermodel.Password);
            if (result.Succeeded)
            {
                await SendConfirmEmail(user);
                return new RegisterResponse() { isSuccess = true };
            }
            else
            {
                var errorlist = result.Errors.Select(e => e.Description);
                return new RegisterResponse() { Errors = errorlist.ToList() };
            }
        }

        // TODO - Change parameter for DTO & Change returning value & Add sending mail & check if user's email is verified
        public async Task<ServiceResponse<ApplicationUser>> ChangePassword(string email, string oldPassword, string newPassword)
        {
            // check if user exists
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return new ServiceResponse<ApplicationUser>() { isSuccess = false };

            // check if password is correct and replace with a new one
            var result = await _userManager.ChangePasswordAsync(user, oldPassword, newPassword);
            if (!result.Succeeded)
            {
                return new ServiceResponse<ApplicationUser>()
                {
                    isSuccess = false,
                    message = result.Errors.ToString()
                };
            }


            return new ServiceResponse<ApplicationUser>() { isSuccess = true };

        }

        public async Task SendConfirmEmail(ApplicationUser user)
        {
            var dbResult = await _emailRepo.InsertOne(new EmailConfirmationToken()
            {
                userId = user.Id,
                token = await _userManager.GenerateEmailConfirmationTokenAsync(user),
                expiredAt = DateTime.Now.AddHours(24)
            });

            var url = _configuration.GetValue<string>("Host")
                      + "api/auth/ConfirmEmail?token="
                      + dbResult.Data.Id;

            await _emailSender.SendActivationEmail(user.Email, url);
        }
    }
}