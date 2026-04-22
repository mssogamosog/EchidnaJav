window.setCardWidth = function (value) {
    document.documentElement.style
        .setProperty('--card-width', value);
};

window.getWindowWidth = () => window.innerWidth;

window.registerResizeHandler = (dotnetHelper) => {
    window.addEventListener("resize", () => {
        dotnetHelper.invokeMethodAsync("OnBrowserResize", window.innerWidth);
    });
};