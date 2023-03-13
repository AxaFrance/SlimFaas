# LightFaas Proof Of Concept
 
 ![image](https://user-images.githubusercontent.com/52236059/224073808-b4517320-3540-46c9-95c2-61928c0bc2e1.png)

## Getting Started

To test dailyclean on your local machine by using kubernetes with Docker Desktop, please use these commands:

```
git clone https://github.com/AxaGuilDEv/dailyclean.git
cd dailyclean/demo
kubectl create namespace lightfaas-demo
kubectl config set-context --current --namespace=lightfaas-demo
# Create a custom service account
kubectl apply -f dailyclean-serviceaccount.yml
# Install dailyclean for the dailyclean service account
kubectl apply -f deployment-dailyclean.yml
# Install three instances of kubernetes-bootcamp
kubectl apply -f deployment-others.yml
```

Now, open your favorite browser and enter the url of dailyclean-api service : http://localhost:30001

Enjoy dailyclean !!!!


## Lightfaas feature:

- Acting as a proxy as openfaas with 2 modes: 
  - Synchronous http://lightfaas/function/myfunction = > HTTP response function  
  - Asynchronous http://lightfaas/async-function/myfunction => HTTP 201
    - Tail in memory or via Redis
- Play the retry 3 times with graduation
- Allows you to limit the number of parallel HTTP requests for each underlying function 
- Expose prometheous metrics (including the volume of messages in each queue)
