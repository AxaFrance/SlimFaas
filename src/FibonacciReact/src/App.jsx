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

function Main({url}) {
    const [states, setStates] = useState([]);
    const [stateInterval, setInterval] = useState(true);
    const [stateSeconds, setSeconds] = useState(0);

    useInterval(() => {
        if(stateInterval) {
            setInterval(false);
            fetch(url + '/status-functions').then((res) => res.json()).then((data) => {
                var result = data.map((item) => {
                    const r = {};
                    r.numberReady = item.NumberReady || 0;
                    r.numberRequested = item.NumberRequested || 0;
                    r.name = item.Name;
                    r.visibility = item.Visibility;
                    r.podType = item.PodType;
                    let status = item.NumberReady === item.NumberRequested ? "ready" : "loading";
                    if (item.NumberRequested === 0) status = "not_started";
                    r.status = status;
                    return r;
                });
                setStates(result);
            }).then(() => {
                setInterval(true);
            });
        }
    }, 100);

    useInterval(() => { setSeconds(stateSeconds+1) }, 1000);

    return (<>
        <h1>Time elapsed: {stateSeconds} seconds</h1>
        <>{states.map(state =>
            <Deployment data={state} url={url} />
        )}</>
    </>)
}

function Deployment({ data, url }) {
  const [stateFibonacci, setFibonacci] = useState({});


  const postFibonacciAsync = ( method = "fibonacci" ) => {
    const start = performance.now();
      setFibonacci({"status": "loading"});
    fetch(url +'/function/' + data.name + "/" + method , {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ "input": 10 })
    }).then((res) => {
        if(res && res.status !== 200) {
            return { "statusCode": res.status};
        }
        return res.json()
    }).then((d) => {
      const end = performance.now();
      const result = {...d, "duration_seconds": ((end - start)/1000).toFixed(2)};
      console.log(result);
      setFibonacci(result);
    });
  }

  const postStartAsync = () => {
        fetch( url +'/wake-function/'+data.name , { method: 'POST', body:"" });
  }

    const publicEventFibonacciAsync = ( method = "fibonacci" ) => {
        setFibonacci({"status": "loading"});
        fetch(url +'/publish-event/fibo-public/' + method , {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ "input": 10 })
        }).then((res) => {
            setFibonacci({ "statusCode": res.status});
        });
    }

    const privateEventFibonacciAsync = ( method = "send-private-fibonacci-event" ) => {
        setFibonacci({"status": "loading"});
        fetch(url +'/function/' + data.name + "/" + method , {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ "input": 10 })
        }).then((res) => {
            setFibonacci({ "statusCode": res.status});
        });
    }

  return (
    <div className="deployment">
      <h2>{data.name}</h2>
        <div>
            {data.name !== "mysql" ?
                <button onClick={() => postFibonacciAsync()}>
                    Post /fibonacci 10
                </button>
                : <></>}
            {(data.name !== "mysql" && data.name !== "fibonacci4") ?
                <button onClick={() => postFibonacciAsync("fibonacci4")}>
                    Post /fibonacci4 10
                </button>
                : <></>}
            <button onClick={() => publicEventFibonacciAsync()}>
                Send event: fibo-public
            </button>
            <button onClick={() => publicEventFibonacciAsync()}>
                Post /send-private-fibonacci-event 10
            </button>
            <button onClick={() => postStartAsync()}>
                Wake up
            </button>
            <p>
                Environment status: <span className={"environment_" + data.status}> {data.status}</span> <br/>
                Request status: {JSON.stringify(stateFibonacci)}
            </p>
        </div>
    </div>
  )
}

export default Main
