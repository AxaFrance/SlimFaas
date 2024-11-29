import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.jsx'
import PlanetSaver from './PlanetSaver.jsx'
import mockFetch from './mockFetch.js'

createRoot(document.getElementById('root')).render(
  <StrictMode>
     <PlanetSaver baseUrl="https://slimfaas/" fetch={mockFetch}>
        <App />
     </PlanetSaver>
  </StrictMode>,
)
