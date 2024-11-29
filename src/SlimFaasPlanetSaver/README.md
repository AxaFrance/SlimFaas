# @axa-fr/slimfaas-planet-saver

![SlimFaas.png](https://github.com/AxaFrance/SlimFaas/blob/main/documentation/SlimFaas.png)

A Vanilla JS project to save the planet. SlimFaas (https://github.com/AxaFrance/slimfaas) is the slimest and simplest Function As A Service on Kubernetes.
It works as a proxy that you can be deployed in your namespace.

SlimFaas API can give to the frontend information about the infrastructure state. **It is a mind changer !**

**Why?**

Because in production instead of setting up 2 replicas of your API backend, you can set up 0 replicas and use an UX that will show the user that the backend is down instead !
**@axa-fr/slimfaas-planet-saver** is here to for doing that easily.

![SlimFaasPlanetSaver.gif](https://github.com/AxaFrance/SlimFaas/blob/main/documentation/SlimfaasPlanetSaver.gif)

## Getting Started

```javascript
npm install @axa-fr/slimfaas-planet-saver
```

Example usage with react :
```javascript
import React, { useState, useEffect } from 'react';
import SlimFaasPlanetSaver from "@axa-fr/slimfaas-planet-saver";

const PlanetSaver = ({ children, baseUrl, fetch }) => {
    const [isFirstStart, setIsFirstStart] = useState(true); // State for first start

    useEffect(() => {
        if (!baseUrl) return;

        const environmentStarter = new SlimFaasPlanetSaver(baseUrl, {
            interval: 2000,
            fetch,
            updateCallback: (data) => {
                const allReady = data.every((item) => item.NumberReady >= 1);
                if (allReady && isFirstStart) {
                    setIsFirstStart(false);
                }
            },
            errorCallback: (error) => {
                console.error('Error detected :', error);
                environmentStarter.updateOverlayMessage('An error occured when starting environment. Please contact an administrator.');
            },
            overlayMessage: 'Starting the environment...',
        });

        // Start polling
        environmentStarter.startPolling();

        // Cleanup
        return () => environmentStarter.cleanup();
    }, [baseUrl, isFirstStart]);

    // During the first start, display a loading message
    if (isFirstStart) {
        return null;
    }

    // Once the environment is started, display the children
    return <>{children}</>;

};

export default PlanetSaver;

```

## Run the demo

```javascript
git clone https://github.com/AxaFrance/slimfaas.git
cd slimfaas/src/SlimFaasPlanetSaver
npm i
npm run dev
```
