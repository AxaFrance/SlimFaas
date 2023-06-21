# SlimFaas : The slimest and simplest Function As A Service [![Continuous Integration](https://github.com/AxaFrance/SlimFaas/actions/workflows/slimfaas-ci.yaml/badge.svg)](https://github.com/AxaFrance/SlimFaas/actions/workflows/slimfaas-ci.yaml) [![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=AxaFrance_SlimFaas&metric=alert_status)](https://sonarcloud.io/dashboard?id=AxaFrance_SlimFaas) [![Reliability](https://sonarcloud.io/api/project_badges/measure?project=AxaFrance_SlimFaas&metric=reliability_rating)](https://sonarcloud.io/component_measures?id=AxaFrance_SlimFaas&metric=reliability_rating) [![Security](https://sonarcloud.io/api/project_badges/measure?project=AxaFrance_SlimFaas&metric=security_rating)](https://sonarcloud.io/component_measures?id=AxaGuilDEv_ml-cli&metric=security_rating) [![Code Corevage](https://sonarcloud.io/api/project_badges/measure?project=AxaFrance_SlimFaas&metric=coverage)](https://sonarcloud.io/component_measures?id=AxaFrance_SlimFaas&metric=Coverage) [![Docker SlimFaas](https://img.shields.io/docker/pulls/axaguildev/slimfaas.svg)](https://hub.docker.com/r/axaguildev/slimfaas/builds)

![SlimFaas.png](documentation%2FSlimFaas.png)

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

## Why SlimFaas ?

We used **OpenFaas** for a long time and we love it.
But we encounter many OpenFaas issues :
- Kubernetes scripts are tightly coupled to OpenFaas syntax
- OpenFaas pro is to expensive for our projects
- OpenFaas need to be installed on a dedicated namespace and configuration was intricate
- OpenFaas Monitoring was not compatible with our Monitoring solution
- It require to configure well NATS for managing fail-over
- Queue configuration is not easy
- The aggressive removes of OpenFaas teams in April 20023 of old images from docker.io create us some production issues

We would like to use **Knative** but:
- We cannot have it because of some internal constraints and security issues. We know now it will be impossible to have it.

So we decide to create **SlimFaas** to have a quick and simple replacement proxy solution that can expose prometeus metrics.
So we could use it with **Keda** to scale to 0 and scale to N (Keda is call and can scale from any prometheus Metrics).

But, we tried in few minutes to scale from SlimFaas and it was a great success.

So, now we have a solution not **coupled** to anything. **SlimFaas** is **simple**, **light** and **fast** with a very cool plug and play !

## How it works ?

Instead of creating many pods, SlimFaas use internally many workers in the same pod:

- **SlimWorker**: Manage asynchronous HTTP requests calls to underlying functions
- **HistorySynchronisationWorker**: Manage history of HTTP requests between the pod and kubernetes
- **ReplicasSynchronizationWorker**: Manage replicas synchronization between the pod and kubernetes
- **MasterWorker**: Elect a master pod to manage kubernetes scale up and down
- **ReplicasScaleWorker**: If master, then scale up and down kubernetes pods

### Build with .NET

Why .NET ?
- .NET is always getting faster and faster : https://www.techempower.com/benchmarks/#section=data-r21
- ASP.NET Core allow to resolve complex use cases with few lines of codes
- .NET is always getting smaller and smaller: https://twitter.com/MStrehovsky/status/1660806238979117056?t=WPrZwi7WrIWi4tjoDUXEgg&s=19


## What Next ?

1. Public Open Source
2. Add a build version without any redis dependencies and allow SlimFaas to manage internal queue
3. Scale up dynamically from SlimFaas
4. Upgrade to .NET8 using AOT => lighter and faster
