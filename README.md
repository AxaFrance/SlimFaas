# SlimFaas : The slimest and simplest Function As A Service [![Continuous Integration](https://github.com/AxaFrance/SlimFaas/actions/workflows/slimfaas-ci.yaml/badge.svg)](https://github.com/AxaFrance/SlimFaas/actions/workflows/slimfaas-ci.yaml) [![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=AxaFrance_SlimFaas&metric=alert_status)](https://sonarcloud.io/dashboard?id=AxaFrance_SlimFaas) [![Reliability](https://sonarcloud.io/api/project_badges/measure?project=AxaFrance_SlimFaas&metric=reliability_rating)](https://sonarcloud.io/component_measures?id=AxaFrance_SlimFaas&metric=reliability_rating) [![Security](https://sonarcloud.io/api/project_badges/measure?project=AxaFrance_SlimFaas&metric=security_rating)](https://sonarcloud.io/component_measures?id=AxaGuilDEv_ml-cli&metric=security_rating) [![Code Corevage](https://sonarcloud.io/api/project_badges/measure?project=AxaFrance_SlimFaas&metric=coverage)](https://sonarcloud.io/component_measures?id=AxaFrance_SlimFaas&metric=Coverage) [![Docker SlimFaas](https://img.shields.io/docker/pulls/axaguildev/slimfaas.svg)](https://hub.docker.com/r/axaguildev/slimfaas/builds)

![SlimFaas.png](https://github.com/AxaFrance/SlimFaas/blob/main/documentation/SlimFaas.png)

Why use SlimFaas ?
- Scale to 0 after a period of inactivity
- Synchronous HTTP calls
- Asynchronous HTTP calls
  - Allows you to limit the number of parallel HTTP requests for each underlying function
- Retry: 3 times with graduation: 2 seconds, 4 seconds, 8 seconds
- Mind Changer: REST API that show the status of your functions and allow to wake up your infrastructure
  - Very useful to inform end users that your infrastructure is starting
- Plug and Play: just deploy a standard pod
  - No impact on your current kubernetes manifests: just add an annotation to the pod you want to auto-scale
- Very **Slim** and very **Fast**

![slim-faas-ram-cpu.png](https://github.com/AxaFrance/SlimFaas/blob/main/documentation/slim-faas-ram-cpu.png)

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
- http://localhost:30021/function/fibonacci1/hello/guillaume
- http://localhost:30021/function/fibonacci2/hello/elodie
- http://localhost:30021/function/fibonacci3/hello/julie

Asynchronous way:
- http://localhost:30021/async-function/fibonacci1/hello/guillaume
- http://localhost:30021/async-function/fibonacci2/hello/elodie
- http://localhost:30021/async-function/fibonacci3/hello/julie

Just wake up function:
- http://localhost:30021/wake-function/fibonacci1
- http://localhost:30021/wake-function/fibonacci2
- http://localhost:30021/wake-function/fibonacci3

Get function status:
- http://localhost:30021/status-function/fibonacci1 => {"NumberReady":1,"numberRequested":1}
- http://localhost:30021/status-function/fibonacci2 => {"NumberReady":1,"numberRequested":1}
- http://localhost:30021/status-function/fibonacci3 => {"NumberReady":1,"numberRequested":1}

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

![sync_http_call.PNG](https://github.com/AxaFrance/slimfaas/blob/main/documentation/sync_http_call.PNG)

### Asynchrounous HTTP call

- Asynchronous http://slimfaas/async-function/myfunction => HTTP 201
  - Tail using SlimData database included in SlimFaas pod

![async_http_call.PNG](https://github.com/AxaFrance/slimfaas/blob/main/documentation/async_http_call.PNG)

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
        # Just add SlimFaas annotation to your pods and that's it !
        SlimFaas/Function: "true"
        SlimFaas/ReplicasMin: "0"
        SlimFaas/ReplicasAtStart: "1"
        SlimFaas/ReplicasStartAsSoonAsOneFunctionRetrieveARequest: "false"
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
kind: StatefulSet
metadata:
  name: slimfaas
spec:
  replicas: 3
  podManagementPolicy: Parallel
  serviceName: slimfaas
  selector:
    matchLabels:
      app: slimfaas
  template:
    metadata:
      labels:
        app: slimfaas
    spec:
      volumes:
        - name: slimfaas-volume
          emptyDir:
            sizeLimit: 10Mi
      serviceAccountName: admin # Use a service account with admin role
      containers:
        - name: slimfaas
          image: docker.io/axaguildev/slimfaas:latest
          livenessProbe:
            httpGet:
              path: /health
              port: 5000
            initialDelaySeconds: 3
            periodSeconds: 10
            timeoutSeconds: 8
          env:
            - name: BASE_FUNCTION_URL
              value: "http://{function_name}.{namespace}.svc.cluster.local:8080"
            - name: BASE_SLIMDATA_URL
              value: "http://{pod_name}.slimfaas.{namespace}.svc.cluster.local:3262/"  # Don't expose this port, it can also be like "http://{pod_ip}:3262/" but if you can use DNS it's better
            - name: SLIMFAAS_PORTS
              value: "5000" # can be like "5000,6000,7000" if you want to expose more ports
            - name: NAMESPACE
              value: "default"
            - name: SLIMDATA_DIRECTORY
              value: "/database"
            # If you want to use just one pod for testing purpose, you can use this env variable
            #- name: SLIMDATA_ALLOW_COLD_START
            #  value: "true" # default equivalent to false, but allow to start a pod alone as a leader
            # If your are not on kubernetes for example docker-compose, you can use this env variable, but you will lose auto-scale
            #- name: MOCK_KUBERNETES_FUNCTIONS
            #  value: "{"Functions":[{"Name":"fibonacci","NumberParallelRequest":1}],"Slimfaas":[{"Name":"slimfaas-1"}]}"

            # Optional, longer is the delay, less CPU and RAM is used
            #- name : HISTORY_SYNCHRONISATION_WORKER_DELAY_MILLISECONDS
            #  value : "500" # default equivalent to 0,5 seconds
            # Optional, longer is the delay, less CPU and RAM is used
            #- name : REPLICAS_SYNCHRONISATION_WORKER_DELAY_MILLISECONDS
            #  value : "2000" # default equivalent to 2 seconds
            # Optional, longer is the delay, less CPU and RAM is used
            #- name : SLIM_WORKER_DELAY_MILLISECONDS
            #  value : "50" # default equivalent to 50 milliseconds
            # Optional, longer is the delay, less CPU and RAM is used
            #- name : SCALE_REPLICAS_WORKER_DELAY_MILLISECONDS
            #  value : "1000" # default equivalent to 1 seconds
            # Optional
            # name : TIME_MAXIMUM_WAIT_FOR_AT_LEAST_ONE_POD_STARTED_FOR_SYNC_FUNCTION
            # value : "10000" # default equivalent to 10 seconds
            # Optional
            # name : POD_SCALED_UP_BY_DEFAULT_WHEN_INFRASTRUCTURE_HAS_NEVER_CALLED
            # value : "false" # default equivalent to false
          volumeMounts:
            - name: slimfaas-volume
              mountPath: /database
          resources:
            limits:
              memory: "76Mi"
              cpu: "400m"
            requests:
              memory: "76Mi"
              cpu: "250m"
          ports:
            - containerPort: 5000
            - containerPort: 3262
  # You can use this section to define a persistent volume claim
  #volumeClaimTemplates:
  #- metadata:
  #    name: slimfaas-volume
  #  spec:
  #    accessModes: [ "ReadWriteOnce" ]
  #    storageClassName: managed-csi # or any other storage class available in your cluster
  #    volumeMode: Filesystem
  #    resources:
  #      requests:
  #        storage: 10Mi
---
apiVersion: v1
kind: Service
metadata:
    name: slimfaas
spec:
    selector:
        app: slimfaas
    ports:
        - name: "http"
          port: 80
          targetPort: 5000
        - name: "slimdata"
          port: 3262
          targetPort: 3262
````


### SlimFaas Annotations with defaults values
- SlimFaas/Function: "true"
  - Activate SlimFaas on this pod, so your pod will be auto-scaled
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
But we encountered many OpenFaas issues :
- Kubernetes scripts are tightly coupled to OpenFaas syntax
- OpenFaas pro is too expensive for our projects
- OpenFaas needs to be installed on a dedicated namespace and configuration was intricate
- OpenFaas monitoring was not compatible with our monitoring solution
- It requires to configure well NATS for managing fail-over
- Queue configuration is not easy
- The aggressive removing of of old images from docker.io by OpenFaas team in April 20023 got us some production issues

We would like to use **Knative** but:
- We cannot use it because of some internal constraints and security issues.

So we decided to create **SlimFaas** to have a quick and simple replacement proxy solution that can expose Prometheus metrics.
Now we have a solution not **coupled** to anything. **SlimFaas** is **simple**, **light**, **fast** and **plug and play** !

## How it works ?

Instead of creating many pods, SlimFaas use internally many workers in the same pod:

- **SlimWorker**: Manage asynchronous HTTP requests calls to underlying functions
- **SlimDataSynchronizationWorker**: Allow to make possible scale up and down SlimData Raft nodes
- **HistorySynchronisationWorker**: Manage history of HTTP requests between the pod and kubernetes
- **ReplicasSynchronizationWorker**: Manage replicas synchronization between the pod and kubernetes
- **ReplicasScaleWorker**: If master, then scale up and down kubernetes pods

**SlimData** is a simple redis like database included inside SlimFaas executable. It is based on **Raft** algorithm offered by awesome https://github.com/dotnet/dotNext library.
By default **SlimData** use a second HTTP port 3262 to expose its API. Don't expose it and keep it internal.

SlimFaas require a least 3 nodes in production. 2 nodes are requires to keep the database in a consistent state.
If you want to use just one pod for testing purpose, you can use this env variable:
- SLIMDATA_ALLOW_COLD_START: "true"

This will allow to start a pod alone as a leader.
SlimFaas can to scale up and down by using classic Horizontal Pod Autoscaler (HPA).

### Build with .NET

Why .NET ?
- .NET is always getting faster and faster : https://www.techempower.com/benchmarks/#section=data-r22
- ASP.NET Core allow to resolve complex use cases with few lines of codes
- .NET is always getting smaller and smaller: https://twitter.com/MStrehovsky/status/1660806238979117056?t=WPrZwi7WrIWi4tjoDUXEgg&s=19


## What Next ?

1. Different scale down mode depending from configuration and current hour
2. Allow to scale down also statefulset pods
3. Scale up dynamically from SlimFaas
