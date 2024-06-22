import React from 'react'
import ReactDOM from 'react-dom/client'
import Main from './App.jsx'
import './index.css'

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <Main url="http://localhost:30021" />

  </React.StrictMode>,
)
