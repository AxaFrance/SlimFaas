import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import PlanetSaver from './PlanetSaver.jsx';
import mockFetch , { setAlternateStatusFunctionsBody, alternateStatusFunctionsBodyOn, alternateStatusFunctionsBodyOff } from './mockFetch.js'
import React from "react";

describe('PlanetSaver Component', () => {
    const baseUrl = 'https://slimfaas/';

    function setDocumentVisibility(state) {
        Object.defineProperty(document, 'visibilityState', {
            value: state,
            writable: true,
        });
        document.dispatchEvent(new Event('visibilitychange'));
    }

    it('Should display SlimFaasPlanetSaver', async () => {
        const handleVisibilityChange = vi.fn();
        render(<PlanetSaver baseUrl={baseUrl} fetch={mockFetch(false)} noActivityTimeout={5000} >Child Component</PlanetSaver>);

        await waitFor(() => screen.getByText('🌳 Starting the environment.... 🌳'));
        expect(screen.getByText('🌳 Starting the environment.... 🌳')).toBeTruthy();
        screen.debug();

        setAlternateStatusFunctionsBody(alternateStatusFunctionsBodyOn);
        await waitFor(() => screen.getByText('Child Component'), { timeout: 4000 });
        expect(screen.getByText('Child Component')).toBeTruthy();
        screen.debug();

        setAlternateStatusFunctionsBody(alternateStatusFunctionsBodyOff);
        setDocumentVisibility('hidden');
        screen.debug();

        setDocumentVisibility('visible');
        await waitFor(() => screen.getByText('Waiting activity to start environment...'), { timeout: 5000 });
        expect(screen.getByText('Waiting activity to start environment...')).toBeTruthy();
        screen.debug();

        document.dispatchEvent(new MouseEvent('mousemove', { clientX: 100, clientY: 100 }));
        await waitFor(() => screen.getByText('🌳 Starting the environment.... 🌳'), { timeout: 10000 });
        expect(screen.getByText('🌳 Starting the environment.... 🌳')).toBeTruthy();
        screen.debug();

    }, {timeout: 40000} );

    it('Should display SlimFaasPlanetSaver Error', async () => {
        render(<PlanetSaver baseUrl={baseUrl} fetch={mockFetch(true, 1)} noActivityTimeout={10000} >Child Component</PlanetSaver>);
        await waitFor(() => screen.getByText('An error occurred when starting environment. Please contact an administrator.'), { timeout: 10000 });
        expect(screen.getByText('An error occurred when starting environment. Please contact an administrator.')).toBeTruthy();
        screen.debug();
    }, {timeout: 20000} );

});
