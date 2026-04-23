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
window.imageObserver = {
    observers: new Map(),

    observe: function (element, dotnetRef) {
        let isVisible = false;

        const observer = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (entry.isIntersecting && !isVisible) {
                    isVisible = true;
                    dotnetRef.invokeMethodAsync("OnVisible").catch(() => { });
                }
                else if (!entry.isIntersecting && isVisible) {
                    isVisible = false;
                    dotnetRef.invokeMethodAsync("OnHidden").catch(() => { });
                }
            });
        }, {
            root: null,
            threshold: 0.01,
            rootMargin: "1000px 0px" // 🔥 preload before visible
        });

        observer.observe(element);
        this.observers.set(element, observer);
    },

    unobserve: function (element) {
        const observer = this.observers.get(element);
        if (observer) {
            observer.disconnect();
            this.observers.delete(element);
        }
    }
};