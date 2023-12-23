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
  const [state, setState] = useState( {"status": "not_started"});
  const [stateInterval, setInterval] = useState(true);
  const [stateSeconds, setSeconds] = useState(0);
  const [stateFibonacci, setFibonacci] = useState( {});
  useInterval(() => {
      if(stateInterval) {
          setInterval(false);
          fetch(url + '/status-function/' + name).then((res) => res.json()).then((data) => {
              data.NumberReady = data.NumberReady || 0;
              data.NumberRequested = data.NumberRequested || 0;
              let status = data.NumberReady === data.NumberRequested ? "ready" : "loading";
              if (data.NumberRequested === 0) status = "not_started";
              setState({status: status});
          }).then(() => {
            setInterval(true);
          });
      }
  }, 1000);

    useInterval(() => { setSeconds(stateSeconds+1) }, 1000);

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
                Seconds: {stateSeconds} <br/>
               Environment status: {state.status} <br/>
               Request status: {JSON.stringify(stateFibonacci)}
            </p>
        </div>
    </>
  )
}

export default App
