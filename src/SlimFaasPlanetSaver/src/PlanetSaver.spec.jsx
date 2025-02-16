﻿import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import PlanetSaver from './PlanetSaver.jsx';
import mockFetch from './mockFetch.js'
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
        render(<PlanetSaver baseUrl={baseUrl} fetch={mockFetch(false)} noActivityTimeout={8000} >Child Component</PlanetSaver>);
        await waitFor(() => screen.getByText('🌳 Starting the environment.... 🌳'));
        screen.debug();
        await waitFor(() => screen.getByText('Child Component'), { timeout: 10000 });
        screen.debug();
        setDocumentVisibility('hidden');
        await waitFor(() => console.log("Wait 10 secondes"), { timeout: 10000 });
        screen.debug();
        setDocumentVisibility('visible');
        await waitFor(() => screen.getByText('Waiting activity to start environment...'), { timeout: 8000 });
        screen.debug();
        // mouve mousse over document with coordinates
        /*document.dispatchEvent(new MouseEvent('mousemove', { clientX: 100, clientY: 100 }));
        document.dispatchEvent(new MouseEvent('mousemove', { clientX: 300, clientY: 200 }));

        await waitFor(() => screen.getByText('🌳 Starting the environment.... 🌳'), { timeout: 10000 });
        screen.debug();*/

    }, {timeout: 40000} );

    it('Should display SlimFaasPlanetSaver Error', async () => {
        render(<PlanetSaver baseUrl={baseUrl} fetch={mockFetch(true, 1)} noActivityTimeout={10000} >Child Component</PlanetSaver>);
        await waitFor(() => screen.getByText('An error occurred when starting environment. Please contact an administrator.'), { timeout: 10000 });
        screen.debug();
    }, {timeout: 20000} );

});
