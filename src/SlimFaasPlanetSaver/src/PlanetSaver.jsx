﻿import React, { useState, useEffect } from 'react';
import SlimFaasPlanetSaver from "./SlimFaasPlanetSaver.js";

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
            },
            overlayStartingMessage: '🌍 Starting the environment.... 🌳',
            overlayNoActivityMessage: 'Waiting activity to start environment...',
            overlayErrorMessage: 'An error occured when starting environment. Please contact an administrator.',
            overlaySecondaryMessage: 'Startup should be fast, but if no machines are available it can take several minutes.'
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
