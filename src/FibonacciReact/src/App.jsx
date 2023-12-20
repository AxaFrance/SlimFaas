import { useState, useEffect, useRef } from 'react'
import './App.css'

function useInterval(callback, delay) {
  const savedCallback = useRef();

  // Remember the latest function.
  useEffect(() => {
    savedCallback.current = callback;
  }, [callback]);

  // Set up the interval.
  useEffect(() => {
    function tick() {
      savedCallback.current();
    }
    if (delay !== null) {
      let id = setInterval(tick, delay);
      return () => clearInterval(id);
    }
  }, [delay]);
}

function App({ name }) {
  const [state, setState] = useState( {"NumberReady":0,"numberRequested":0});
  useInterval(() => {
    fetch('http://localhost:30021/status-function/'+name).then((res) => res.json()).then((data) => {
      console.log(data);
      setState(data);
    });
  }, 2000);

  const postFibonacciAsync = () => {
    fetch('http://localhost:30021/function/'+name, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({"input": 42})
    }).then((res) => res.json()).then((data) => {
      console.log(data);
      setState(data);
    });
  }

  return (
    <>
      <h2>{name}</h2>
      <div className="card">
        <button onClick={() => postFibonacciAsync()}>
          Post Fibonacci(42)
        </button>
        <p>
          {state.NumberReady} of {state.numberRequested} numbers ready
        </p>
      </div>
    </>
  )
}

export default App
