using Microsoft.AspNetCore.Components;

namespace TicketPrime.Web.Client.Services;

public static class SessionGuardExtensions
{
    public static async Task<bool> EnsureAuthenticatedAsync(
        this SessionService session,
        NavigationManager navigation,
        string redirectTo = "/login",
        bool replace = true)
    {
        await session.CarregarAsync();
        if (session.EstaLogado)
            return true;

        navigation.NavigateTo(redirectTo, replace: replace);
        return false;
    }

    public static async Task<bool> EnsureAdminAsync(
        this SessionService session,
        NavigationManager navigation,
        string unauthenticatedRedirectTo = "/login",
        string unauthorizedRedirectTo = "/",
        bool replace = true)
    {
        await session.CarregarAsync();
        if (session.EhAdmin)
            return true;

        navigation.NavigateTo(
            session.EstaLogado ? unauthorizedRedirectTo : unauthenticatedRedirectTo,
            replace: replace);
        return false;
    }
}
