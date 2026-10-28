window.blazorCulture = {
    set: function (value) {
        const cookieValue = "c=" + value + "|uic=" + value;
        const currentCookie = document.cookie
            .split("; ")
            .find(cookie => cookie.startsWith(".AspNetCore.Culture="));
        const changed = currentCookie !== ".AspNetCore.Culture=" + cookieValue;
        document.cookie = ".AspNetCore.Culture=" + cookieValue + "; path=/";
        return changed;
    }
};