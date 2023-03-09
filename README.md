# LightFaas Proof Of Concept
 
 ![image](https://user-images.githubusercontent.com/52236059/224073808-b4517320-3540-46c9-95c2-61928c0bc2e1.png)

## Lightfaas feature:

- Acting as a proxy as openfaas with 2 modes: 
  - Synchronous http://lightfaas/function/myfunction = > HTTP response function  
  - Asynchronous http://lightfaas/async-function/myfunction => HTTP 201
    - Tail in memory or via Redis
- Play the retry 3 times with graduation
- Allows you to limit the number of parallel HTTP requests for each underlying function 
- Expose prometheous metrics (including the volume of messages in each queue)
