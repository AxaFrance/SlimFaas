# SlimFaas : The slimest and simplest Function As A Service

Why use SlimFaas ?
- Scale to 0 after a period of inactivity
- Synchronous HTTP calls
- Asynchronous HTTP calls
  - Allows you to limit the number of parallel HTTP requests for each underlying function
- Retry: 3 times with graduation: 2 seconds, 4 seconds, 8 seconds
- Simple to install: just add a classic pod
  - No impact on kubernetes scripts: just add annotation to a pod you want to be auto-scale
- Very Slim and very Fast


 ![image](https://user-images.githubusercontent.com/52236059/224073808-b4517320-3540-46c9-95c2-61928c0bc2e1.png)

## Getting Started

To test slimfaas on your local machine by using kubernetes with Docker Desktop, please use these commands:
 
```
git clone https://github.com/AxaFrance/slimfaas.git
cd slimfaas/demo
kubectl create namespace slimfaas-demo
kubectl config set-context --current --namespace=slimfaas-demo
# Create a custom service account
kubectl apply -f dailyclean-serviceaccount.yml
# Install dailyclean for the dailyclean service account
kubectl apply -f deployment-dailyclean.yml
# Install three instances of kubernetes-bootcamp
kubectl apply -f deployment-others.yml
```

Now, open your favorite browser and enter the url of dailyclean-api service : http://localhost:30001

Enjoy slimfaas !!!!

## How it works

### Synchrounous HTTP call
![sync_http_call.PNG](documentation%2Fsync_http_call.PNG)

### Asynchrounous HTTP call
![async_http_call.PNG](documentation%2Fasync_http_call.PNG)


- SlimFaas act as a proxy as openfaas with 2 modes: 
  - Synchronous http://slimfaas/function/myfunction = > HTTP response function  
  - Asynchronous http://slimfaas/async-function/myfunction => HTTP 201
    - Tail in memory or via Redis

## What Next ?

- Clean Code & Unit Tests
- Get full working Get Started Demo
- Public Open Source
- Scale up from volume message in queue and message rate
- Add a build version without any redis dependencies and allow SlimFaas to manage internal queue
- Upgrade to .NET8 using AOT => lighter and faster