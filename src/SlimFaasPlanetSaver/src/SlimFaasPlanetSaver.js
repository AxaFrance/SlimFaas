const normalizeBaseUrl = (url) => {
    let tempUrl = url;
    if (tempUrl.endsWith('/')) tempUrl = tempUrl.slice(0, -1);
    return tempUrl;
}

export default class SlimFaasPlanetSaver {
    constructor(baseUrl, options = {}) {
        this.baseUrl = normalizeBaseUrl(baseUrl);
        this.updateCallback = options.updateCallback || (() => {});
        this.errorCallback = options.errorCallback || (() => {});
        this.interval = options.interval || 5000;
        this.overlayMessage = options.overlayMessage || 'Starting in progress...';
        this.fetch = options.fetch || fetch;
        this.intervalId = null;
        this.isDocumentVisible = !document.hidden;
        this.overlayElement = null;
        this.styleElement = null;
        this.isReady = false;

        // Initialize lastMouseMoveTime and bind the handler
        this.lastMouseMoveTime = Date.now();
        this.handleMouseMove = this.handleMouseMove.bind(this);
        this.handleVisibilityChange = this.handleVisibilityChange.bind(this);

        document.addEventListener('visibilitychange', this.handleVisibilityChange);
        document.addEventListener('mousemove', this.handleMouseMove);

        this.createOverlay();
        this.injectStyles();

        this.events = document.createElement('div');
    }

    handleMouseMove() {
        this.lastMouseMoveTime = Date.now();
    }

    handleVisibilityChange() {
        this.isDocumentVisible = !document.hidden;
        if (this.isDocumentVisible) {
            this.startPolling();
        } else {
            this.stopPolling();
        }
    }

    async wakeUpPods(data) {
        const wakePromises = data
            .filter((item) => item.NumberReady === 0)
            .map((item) =>
                this.fetch(`${this.baseUrl}/wake-function/${item.Name}`, {
                    method: 'POST',
                })
            );

        try {
            await Promise.all(wakePromises);
        } catch (error) {
            console.error("Error waking up pods:", error);
        }
    }

    async fetchStatus() {
        try {
            const response = await this.fetch(`${this.baseUrl}/status-functions`);
            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }
            const data = await response.json();

            const allReady = data.every((item) => item.NumberReady >= 1);
            this.setReadyState(allReady);

            this.updateCallback(data);

            const now = Date.now();
            const mouseMovedRecently = now - this.lastMouseMoveTime <= 60000; // 1 minute in milliseconds

            // Modified condition to check mouse movement
            if (!this.isDocumentVisible || mouseMovedRecently) {
                await this.wakeUpPods(data);
            }
        } catch (error) {
            const errorMessage = error.message;
            this.errorCallback(errorMessage);

            this.triggerEvent('error', { message: errorMessage });

            console.error('Error getting slimfaas data:', errorMessage);
        } finally {
            this.intervalId = setTimeout(() => {
                this.fetchStatus();
            }, this.interval);
        }
    }

    setReadyState(isReady) {
        this.isReady = isReady;
        if (isReady) {
            this.hideOverlay();
        } else {
            this.showOverlay();
        }
    }

    startPolling() {
        if (this.intervalId || !this.baseUrl) return;

        this.fetchStatus();

        this.intervalId = setTimeout(() => {
            this.fetchStatus();
        }, this.interval);
    }

    stopPolling() {
        if (this.intervalId) {
            clearTimeout(this.intervalId);
            this.intervalId = null;
        }
    }

    injectStyles() {
        const cssString = `
          .environment-overlay {
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            cursor: not-allowed;
            height: 100%;
            background-color: rgba(0, 0, 0, 0.5);
            display: flex;
            justify-content: center;
            align-items: center;
            color: white;
            font-size: 1.5rem;
            font-weight: bold;
            z-index: 1000;
            visibility: hidden;
          }

          .environment-overlay.visible {
            visibility: visible;
          }
        `;

        this.styleElement = document.createElement('style');
        this.styleElement.textContent = cssString;
        document.head.appendChild(this.styleElement);
    }

    createOverlay() {
        this.overlayElement = document.createElement('div');
        this.overlayElement.className = 'environment-overlay';
        this.overlayElement.innerText = this.overlayMessage;
        document.body.appendChild(this.overlayElement);
    }

    showOverlay() {
        if (this.overlayElement) {
            this.overlayElement.classList.add('visible');
        }
    }

    hideOverlay() {
        if (this.overlayElement) {
            this.overlayElement.classList.remove('visible');
        }
    }

    updateOverlayMessage(newMessage) {
        if (this.overlayElement) {
            this.overlayElement.innerText = newMessage;
        }
    }

    triggerEvent(eventName, detail) {
        const event = new CustomEvent(eventName, { detail });
        this.events.dispatchEvent(event);
    }

    cleanup() {
        this.stopPolling();
        document.removeEventListener('visibilitychange', this.handleVisibilityChange);
        document.removeEventListener('mousemove', this.handleMouseMove); // Remove mousemove listener
        if (this.overlayElement) {
            document.body.removeChild(this.overlayElement);
        }
        if (this.styleElement) {
            document.head.removeChild(this.styleElement);
        }
    }
}
