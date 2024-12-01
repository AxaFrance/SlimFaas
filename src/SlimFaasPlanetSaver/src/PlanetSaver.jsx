﻿import React, { useState, useEffect, useRef } from 'react';
import SlimFaasPlanetSaver from "./SlimFaasPlanetSaver.js";

const PlanetSaver = ({ children, baseUrl, fetch }) => {
    const [isFirstStart, setIsFirstStart] = useState(true);
    const environmentStarterRef = useRef(null);

    useEffect(() => {
        if (!baseUrl) return;

        if (environmentStarterRef.current) return;

        const instance = new SlimFaasPlanetSaver(baseUrl, {
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
            overlayStartingMessage: '🌳 Starting the environment.... 🌳',
            overlayNoActivityMessage: 'Waiting activity to start environment...',
            overlayErrorMessage: 'An error occurred when starting environment. Please contact an administrator.',
            overlaySecondaryMessage: 'Startup should be fast, but if no machines are available it can take several minutes.',
            overlayLoadingIcon: '🌍',
            overlayErrorSecondaryMessage: 'If the error persists, please contact an administrator.'
        });

        environmentStarterRef.current = instance;

        // Initialiser les effets de bord
        instance.initialize();
        instance.startPolling();

        return () => {
            instance.cleanup();
            environmentStarterRef.current = null;
        };
    }, [baseUrl]);

    if (isFirstStart) {
        return null;
    }

    return <>{children}</>;
};

export default PlanetSaver;
