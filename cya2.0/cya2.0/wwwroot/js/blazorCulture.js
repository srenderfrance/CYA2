window.blazorCulture = {
    set: function (value) {
        document.cookie = ".AspNetCore.Culture=c=" + value + "|uic=" + value + "; path=/";
    }
};