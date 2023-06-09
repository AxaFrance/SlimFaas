# SlimFaas : The slimest and simplest Function As A Service [![Continuous Integration]

[![Continuous Integration](https://github.com/AxaFrance/slimfaas/actions/workflows/slimfaas-ci.yml/badge.svg)](https://github.com/AxaFrance/slimfaas/actions/workflows/slimfaas-ci.yml)

Why use SlimFaas ?
- Scale to 0 after a period of inactivity
- Synchronous HTTP calls
- Asynchronous HTTP calls
  - Allows you to limit the number of parallel HTTP requests for each underlying function
- Retry: 3 times with graduation: 2 seconds, 4 seconds, 8 seconds
- Simple to install: just add a classic pod
  - No impact on kubernetes scripts: just add annotation to a pod you want to be auto-scale
- Very Slim and very Fast

![slim-faas-ram-cpu.png](documentation%2Fslim-faas-ram-cpu.png)

## Getting Started with Kubernetes

To test slimfaas on your local machine by using kubernetes with Docker Desktop, please use these commands:
 
```bash
git clone https://github.com/AxaFrance/slimfaas.git
cd slimfaas/demo
kubectl create namespace slimfaas-demo
kubectl config set-context --current --namespace=slimfaas-demo
# Create a custom service account for slimfaas
# SlimFaas require to ba able to request Kubernetes API
kubectl apply -f slimfaas-serviceaccount.yml
# Install slimfaas pod
kubectl apply -f deployment-slimfaas.yml
# Install three instances of fibonacci functions
# fibonacci1, fibonacci2 and fibonacci3
kubectl apply -f deployment-functions.yml
```

Now, you can access your pod via SlimFaas proxy:

Synchronous way:
- http://localhost:30020/function/fibonacci1/hello/guillaume
- http://localhost:30020/function/fibonacci2/hello/elodie
- http://localhost:30020/function/fibonacci3/hello/julie

Asynchronous way:
- http://localhost:30020/async-function/fibonacci1/hello/guillaume
- http://localhost:30020/async-function/fibonacci2/hello/elodie
- http://localhost:30020/async-function/fibonacci3/hello/julie

Just wake up function:
- http://localhost:30020/wake-function/fibonacci1
- http://localhost:30020/wake-function/fibonacci2
- http://localhost:30020/wake-function/fibonacci3


Enjoy slimfaas !!!!


## Getting Started with docker-compose

To test slimfaas on your local machine by using kubernetes with Docker Desktop, please use these commands:

```bash
git clone https://github.com/AxaFrance/slimfaas.git
cd slimfaas
docker-compose up
```

Now, you can access your pod via SlimFaas proxy:

- http://localhost:5020/function/fibonacci/hello/guillaume

Enjoy slimfaas !!!!

## How it works

SlimFaas act as an HTTP proxy with 2 modes: 

### Synchrounous HTTP call

- Synchronous http://slimfaas/function/myfunction = > HTTP response function

![sync_http_call.PNG](documentation%2Fsync_http_call.PNG)

### Asynchrounous HTTP call

- Asynchronous http://slimfaas/async-function/myfunction => HTTP 201
  - Tail in memory or via Redis

![async_http_call.PNG](documentation%2Fasync_http_call.PNG)

### Wake HTTP call

- Wake http://slimfaas/wake-function/myfunction => HTTP 200
  - Wake up a function


### Build with .NET

Why .NET ?
- .NET is always getting faster and faster : https://www.techempower.com/benchmarks/#section=data-r21
- ASP.NET Core allow to resolve complex use cases with few lines of codes
- .NET is always getting smaller and smaller: https://twitter.com/MStrehovsky/status/1660806238979117056?t=WPrZwi7WrIWi4tjoDUXEgg&s=19

## How to install

1. Add SlimFaas annotations to your pods
2. Add SlimFaas pod
3. Have fun !

sample-deployment.yaml
````yaml 
apiVersion: apps/v1
kind: Deployment
metadata:
  name: fibonacci1
spec:
  selector:
    matchLabels:
      app: fibonacci1
  template:
    metadata:
      labels:
        app: fibonacci1
      annotations:
        # Just add SlimFaas annotation to your pods and that it !
        SlimFaas/Function: "true" 
        SlimFaas/ReplicasMin: "0"
        SlimFaas/ReplicasAtStart: "1"
        SlimFaas/ReplicasStartAsSoonAsOneFunctionRetrieveARequest: "true"
        SlimFaas/TimeoutSecondBeforeSetReplicasMin: "300"
        SlimFaas/NumberParallelRequest : "10"
    spec:
      serviceAccountName: default
      containers:
        - name: fibonacci1
          image: docker.io/axaguildev/fibonacci:latest
          resources:
            limits:
              memory: "96Mi"
              cpu: "50m"
            requests:
              memory: "96Mi"
              cpu: "10m"
          ports:
            - containerPort: 8080
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: slimfaas
spec:
  selector:
    matchLabels:
      app: slimfaas
  template:
    metadata:
      annotations:
        prometheus.io/path: /metrics
        prometheus.io/port: '5000'
        prometheus.io/scrape: 'true'
      labels:
        app: slimfaas
    spec:
      serviceAccountName: admin # Use a service account with admin role
      containers:
        - name: slimfaas
          image: docker.io/axaguildev/slimfaas:latest
          livenessProbe:
            httpGet:
              path: /health
              port: 5000
            initialDelaySeconds: 1
            periodSeconds: 10
            timeoutSeconds: 8
          env:
            - name: BASE_FUNCTION_URL
              value: "http://{function_name}.default.svc.cluster.local:8080"
            - name: ASPNETCORE_URLS
              value: http://+:5000
            - name: NAMESPACE
              value: "default"
            # If you want to use Redis use this env variable and comment MOCK_REDIS
            #- name: REDIS_CONNECTION_STRING 
            #  value: "redis-ha-haproxy:6379"
            - name: MOCK_REDIS
              value: "true"
            # If your are not on kubernetes for example docker-compose, you can use this env variable, you will loose auto-scale
            #- name: MOCK_KUBERNETES_FUNCTIONS 
            #  value: "{\"Functions\":[{\"Name\":\"kubernetes-bootcamp1\",\"NumberParallelRequest\":1}]}"
          resources:
            limits:
              memory: "76Mi"
              cpu: "400m"
            requests:
              memory: "76Mi"
              cpu: "250m"
          ports:
            - containerPort: 5000

````


### SlimFaas Annotations with defaults values
- SlimFaas/Function: "true" 
  - Activate SlimFaas on this pod, so your pod will be auto-scale
- SlimFaas/ReplicasMin: "0"
  - Scale down to this value after a period of inactivity
- SlimFaas/ReplicasAtStart: "1"
  - Scale up to this value at start
- SlimFaas/ReplicasStartAsSoonAsOneFunctionRetrieveARequest: "true"
  - Scale up this pod as soon as one function retrieve a request
- SlimFaas/TimeoutSecondBeforeSetReplicasMin: "300" 
  - Scale down to SlimFaas/ReplicasMin after this period of inactivity in seconds
- SlimFaas/NumberParallelRequest : "10"
  - Limit the number of parallel HTTP requests for each underlying function

## What Next ?

1. Public Open Source
2. Add a build version without any redis dependencies and allow SlimFaas to manage internal queue
3. Upgrade to .NET8 using AOT => lighter and faster
