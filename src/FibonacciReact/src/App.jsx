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

function App({ name, url }) {
  const [state, setState] = useState( {"NumberReady":0,"NumberRequested":0});
  const [stateFibonacci, setFibonacci] = useState( {});
  useInterval(() => {
    fetch( url + '/status-function/'+name).then((res) => res.json()).then((data) => {
      setState(data);
    });
  }, 2000);

  const postFibonacciAsync = () => {
    const start = performance.now();
      setFibonacci({"status": "loading"});
    fetch( url +'/function/'+name + "/fibonacci", {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ "input": 10 })
    }).then((res) => res.json() ).then((data) => {
      const end = performance.now();
      const result = {...data, "duration_seconds": (end - start)/1000};
      console.log(result);
        setFibonacci(result);
    });
  }

  const postStartAsync = () => {
        fetch( url +'/wake-function/'+name , { method: 'POST', body:"" });
  }

  return (
    <>
      <h2>{name}</h2>
        <div className="card">
            <button onClick={() => postFibonacciAsync()}>
                Post Fibonacci(10)
            </button>
            <button onClick={() => postStartAsync()}>
                Wake up
            </button>
            <p>
                {state.NumberReady} of {state.NumberRequested} numbers ready <br/>
                {JSON.stringify(stateFibonacci)}
            </p>
        </div>
    </>
  )
}

export default App
