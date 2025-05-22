using Microsoft.AspNetCore.Mvc.RazorPages;
// using Microsoft.AspNetCore.Authentication; // This can likely be removed or will remain greyed out

namespace newcya.Pages.Account // Adjust namespace to match your project
{
    public class LoginModel : PageModel
    {
        public void OnGet()
        {
            // This OnGet method is not strictly necessary for the initial challenge
            // but can be left here. The code in Login.cshtml handles the challenge.
        }

        public async Task OnGetCallbackAsync(string returnUrl = null)
        {
            // This method is hit after Google redirects back.
            // The authentication middleware handles processing the Google response
            // and triggering your OnCreatingTicket event in Program.cs.

            // After successful authentication and your OnCreatingTicket logic
            // has run, the user will be authenticated. You can now redirect them
            // to the intended returnUrl or a default page.

            // The AuthenticationProperties used in OnGetAsync will automatically
            // carry the returnUrl through the authentication process.

            // You can optionally retrieve the authentication result again here
            // if needed, but the user should already be authenticated at this point.
            // var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Redirect the user to the original returnUrl or a default page
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                Response.Redirect(returnUrl);
            }
            else
            {
                Response.Redirect("/"); // Redirect to your home page or a default dashboard
            }
        }
    }
}

