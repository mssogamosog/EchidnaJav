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
window.infiniteScroll = {
    observer: null,

    observe: function (element, dotnetRef) {
        this.observer = new IntersectionObserver(entries => {
            if (entries[0].isIntersecting) {
                // When the trigger div enters the viewport (plus margin), tell C#
                dotnetRef.invokeMethodAsync("OnScrollToBottom").catch(() => { });
            }
        }, {
            root: null,
            rootMargin: "600px", // Trigger 600px before reaching the bottom
            threshold: 0.1
        });

        this.observer.observe(element);
    },

    unobserve: function (element) {
        if (this.observer) {
            this.observer.disconnect();
            this.observer = null;
        }
    }
};
window.getGridColumns = function () {
    const grid = document.querySelector('.movie-grid');
    if (!grid) return 1;

    // getComputedStyle returns the exact rendered tracks, e.g., "200px 200px 200px"
    const style = window.getComputedStyle(grid);
    const columns = style.gridTemplateColumns.split(' ').length;

    return columns > 0 ? columns : 1;
};
window.getScrollPos = function () {
    const el = document.querySelector('.content-area');
    return el ? el.scrollTop : 0;
};

window.setScrollPos = function (pos) {
    const el = document.querySelector('.content-area');
    if (el) el.scrollTop = pos;
};
window.registerGlobalKeyHandler = function (dotNetHelper) {
    document.addEventListener('keydown', function (e) {
        // 1. Ignore keystrokes if the user is typing in an input box or textarea
        const targetTag = e.target.tagName.toLowerCase();
        if (targetTag === 'input' || targetTag === 'textarea') {
            return;
        }

        // 2. Check for Left/Right arrows and notify Blazor
        if (e.key === 'ArrowLeft') {
            dotNetHelper.invokeMethodAsync('OnGlobalArrowLeft');
        } else if (e.key === 'ArrowRight') {
            dotNetHelper.invokeMethodAsync('OnGlobalArrowRight');
        }
    });
};

window.getScrollInfo = (id) => {
    const e = document.getElementById(id);
    if (!e) return { scrollLeft: 0, scrollWidth: 0, clientWidth: 0 };
    return {
        scrollLeft: e.scrollLeft,
        scrollWidth: e.scrollWidth,
        clientWidth: e.clientWidth
    };
};
window.clickOutsideHandler = {
    addEvent: function (container, dotNetHelper) {
        // Define the listener
        const listener = function (e) {
            // If the click happened OUTSIDE the container, tell C#
            if (container && !container.contains(e.target)) {
                dotNetHelper.invokeMethodAsync('OnClickedOutside');
            }
        };

        // Attach the listener to the document
        document.addEventListener('click', listener);

        // Store the listener on the HTML element so we can remove it later
        container._clickOutsideListener = listener;
    },
    removeEvent: function (container) {
        // Clean up the memory when the page is destroyed
        if (container && container._clickOutsideListener) {
            document.removeEventListener('click', container._clickOutsideListener);
            delete container._clickOutsideListener;
        }
    }
};