import { useState } from 'react'
import reactLogo from './assets/react.svg'
import slimfaasLogo from './assets/SlimFaas.png'
import './App.css'

function App() {
  const [count, setCount] = useState(0)

  return (
    <>
      <div>
        <a href="https://github.com/AxaFrance/slimfaas" target="_blank">
          <img src={slimfaasLogo} className="logo" alt="Slimfaas logo" />
        </a>
      </div>
      <h1>@axa-fr/slimfaas-planet-saver</h1>
      <div className="card">
        <button onClick={() => setCount((count) => count + 1)}>
          count is {count}
        </button>
      </div>
      <p className="read-the-docs">
        Click on the SlimFaas logo to learn more
      </p>
    </>
  )
}

export default App
