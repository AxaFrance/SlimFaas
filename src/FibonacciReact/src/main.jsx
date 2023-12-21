import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App.jsx'
import './index.css'

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App name='fibonacci1' url="http://localhost:30021" />
    <App name='fibonacci2' url="http://localhost:30022" />
    <App name='fibonacci3' url="http://localhost:30023" />
  </React.StrictMode>,
)
