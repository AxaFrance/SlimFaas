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
        this.overlayStartingMessage = options.overlayStartingMessage || '🌍 Starting the environment.... 🌳';
        this.overlayNoActivityMessage = options.overlayNoActivityMessage || 'Waiting activity to start environment...';
        this.overlayErrorMessage = options.overlayErrorMessage || 'Une erreur est survenue lors du démarrage de l\'environnement. Veuillez contacter un administrateur.';
        this.overlaySecondaryMessage = options.overlaySecondaryMessage || 'Startup should be fast, but if no machines are available it can take several minutes.';
        this.fetch = options.fetch || fetch;
        this.intervalId = null;
        this.isDocumentVisible = !document.hidden;
        this.overlayElement = null;
        this.spanElement = null; // Nouvel élément pour le <span>
        this.styleElement = null;
        this.isReady = false;

        // Initialisation du temps du dernier mouvement de souris et liaison du gestionnaire
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
            console.error("Erreur lors du réveil des pods :", error);
        }
    }

    async fetchStatus() {
        try {
            const response = await this.fetch(`${this.baseUrl}/status-functions`);
            if (!response.ok) {
                throw new Error(`Erreur HTTP ! statut : ${response.status}`);
            }
            const data = await response.json();

            const allReady = data.every((item) => item.NumberReady >= 1);
            this.setReadyState(allReady);

            this.updateCallback(data);

            const now = Date.now();
            const mouseMovedRecently = now - this.lastMouseMoveTime <= 60000; // 1 minute en millisecondes

            if (!allReady && this.isDocumentVisible && !mouseMovedRecently) {
                this.updateOverlayMessage(this.overlayNoActivityMessage, 'waiting-action');
            } else if (!this.isDocumentVisible || mouseMovedRecently) {
                this.updateOverlayMessage(this.overlayStartingMessage, 'waiting');
                await this.wakeUpPods(data);
            }
        } catch (error) {
            const errorMessage = error.message;
            this.updateOverlayMessage(this.overlayErrorMessage, 'error');
            this.errorCallback(errorMessage);
            this.triggerEvent('error', { message: errorMessage });
            console.error('Erreur lors de la récupération des données slimfaas :', errorMessage);
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
            background-color: rgba(0, 0, 0, 0.8);
            display: flex;
            flex-direction: column;
            justify-content: center;
            align-items: center;
            color: white;
            font-size: 2rem;
            font-weight: bold;
            z-index: 1000;
            visibility: hidden;
            text-align: center;
          }

          .environment-overlay.visible {
            visibility: visible;
          }

          .environment-overlay .main-message {
            display: flex;
            align-items: center;
            gap: 0.5rem;
            color: white;
          }

          .environment-overlay .secondary-message {
            font-size: 1.2rem;
            font-weight: normal;
            margin-top: 1rem;
          }

          .environment-overlay--waiting{
            color: white;
          }

          .environment-overlay--waiting-action  {
            color: lightyellow;
          }
          .environment-overlay--waiting-action .secondary-message  {
            visibility: hidden;
          }

          .environment-overlay--error  {
            color: lightred;
          }
          .environment-overlay--error .secondary-message  {
            visibility: hidden;
          }

        `;

        this.styleElement = document.createElement('style');
        this.styleElement.textContent = cssString;
        document.head.appendChild(this.styleElement);
    }

    createOverlay() {
        this.overlayElement = document.createElement('div');
        this.overlayElement.className = 'environment-overlay';

        // Créer un élément <span> pour le texte et les icônes
        this.spanElement = document.createElement('span');
        this.spanElement.innerHTML = `${this.overlayStartingMessage}`;

        // Créer un élément <span> pour le second message
        this.secondarySpanElement = document.createElement('span');
        this.secondarySpanElement.className = 'secondary-message';
        this.secondarySpanElement.innerText = this.overlaySecondaryMessage;

        // Ajouter le <span> à l'overlay
        this.overlayElement.appendChild(this.spanElement);
        this.overlayElement.appendChild(this.secondarySpanElement);

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

    updateOverlayMessage(newMessage, status = 'waiting') {
        if (this.spanElement) {
            this.spanElement.innerHTML = `${newMessage}`;
        }
        if (this.overlayElement) {
            this.overlayElement.classList.remove('environment-overlay--error', 'environment--overlay-waiting', 'environment-overlay--waiting-action');
            this.overlayElement.classList.add("environment-overlay--"+status);
        }
    }

    triggerEvent(eventName, detail) {
        const event = new CustomEvent(eventName, { detail });
        this.events.dispatchEvent(event);
    }

    cleanup() {
        this.stopPolling();
        document.removeEventListener('visibilitychange', this.handleVisibilityChange);
        document.removeEventListener('mousemove', this.handleMouseMove);
        if (this.overlayElement) {
            document.body.removeChild(this.overlayElement);
        }
        if (this.styleElement) {
            document.head.removeChild(this.styleElement);
        }
    }
}
