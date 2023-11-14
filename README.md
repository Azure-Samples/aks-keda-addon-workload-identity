# kedaaddon-AKS-WI-sample
This is a sample to deploy an Azure Function Service Bus trigger using KEDA Add-on for AKS for scaling and connecting to Azure Service Bus using workload identity for AKS
Note: In this sample Az CLI and Azure Portal are being used.

1. Login to Az CLI
   ```sh
   az login
   ```
3. Set the subscription to which the AKS cluster will be deployed to
   ```sh
   az set account -s <sub id>
   ```
5. Create a resource group using az group create command
   ```sh
   az group create --name <myResourceGroup> --location <eastus>
   ```
6.  Create a new AKS cluster using the az aks create command and enable the KEDA add-on using the --enable-keda flag.
   ```sh
    az aks create --resource-group <myResourceGroup> --name <myAKSCluster> --enable-keda
 ```
7. Get the credentials for your AKS cluster using the az aks get-credentials command.
   ```sh
   az aks get-credentials --resource-group <myResourceGroup> --name <myAKSCluster>
   ```
8. Verify the KEDA add-on is installed on your cluster using the az aks show command and set the --query parameter to workloadAutoScalerProfile.keda.enabled.
   ```sh
   az aks show -g <myResourceGroup> --name <myAKSCluster> --query "workloadAutoScalerProfile.keda.enabled"
   The output shows as "true" if the KEDA add-on is installed on the cluster
   ```
9. Verify the KEDA add-on is running on your cluster using the kubectl get pods command.
    ```sh
    kubectl get pods -n kube-system
    ```
10. Now lets create a local Azure Function Service Bus trigger docker project , build and push the image to the container registry and finally deploy to the AKS cluster enabled with KEDA add-on. You may fork the sample Function app from this project or create an Azure Function from template.
    ```sh
    func init <ServiceBusProj> --worker-runtime dotnet-isolated --docker --target-framework net7.0
    func new --name <ServiceBusFunc> --template "ServiceBusQueueTrigger"
    dotnet add package Microsoft.Azure.WebJobs.Extensions.ServiceBus --version 5.2.0
    docker build --tag <registry/projname>:<version> .
    docker push <registry/projname>:<version>
    func kubernetes deploy --name <projname>  --image-name <registry/projname>:<version> --dry-run >> kedafuncproj_deployment.yaml
    ```
11. Workloads deployed on an Azure Kubernetes Services (AKS) cluster require Microsoft Entra application credentials or managed identities to access Microsoft Entra protected resources,such as Azure Service Bus in this case.
[Microsoft Entra Workload ID](https://learn.microsoft.com/en-us/azure/active-directory/develop/workload-identities-overview) uses [Service Account Token Volume Projection](https://kubernetes.io/docs/tasks/configure-pod-container/configure-service-account/#serviceaccount-token-volume-projection) enabling pods to use a Kubernetes identity (that is, a service account). A Kubernetes token is issued and [OIDC federation](https://kubernetes.io/docs/reference/access-authn-authz/authentication/#openid-connect-tokens) enables Kubernetes applications to access Azure resources securely with Microsoft Entra ID based on annotated service accounts.

Run the following commands to create these variables. Replace the default values for RESOURCE_GROUP, LOCATION, SERVICE_ACCOUNT_NAME, SUBSCRIPTION, USER_ASSIGNED_IDENTITY_NAME, FEDERATED_IDENTITY_CREDENTIAL_NAME and KEDA_FEDERATED_IDENTITY_CREDENTIAL_NAME
```sh
export RESOURCE_GROUP="myResourceGroup"
export LOCATION="westcentralus"
export SERVICE_ACCOUNT_NAMESPACE="default"
export SERVICE_ACCOUNT_NAME="workload-identity-sa"
export SUBSCRIPTION="$(az account show --query id --output tsv)"
export USER_ASSIGNED_IDENTITY_NAME="myIdentity"
export FEDERATED_IDENTITY_CREDENTIAL_NAME="myFedIdentity"
export KEDA_FEDERATED_IDENTITY_CREDENTIAL_NAME="KedaFedIdentity"
```

13. Update the AKS cluster using the az aks update command with the --enable-oidc-issuer and the --enable-workload-identity parameter to use the OIDC Issuer and enable workload identity. The following example updates a cluster named myAKSCluster:
```sh
az aks update -g "${RESOURCE_GROUP}" -n myAKSCluster --enable-oidc-issuer --enable-workload-identity
```
13. Get the OIDC Issuer URL and save it to an environmental variable, run the following command. Replace the default value for the arguments -n, which is the name of the cluster:
    ```sh
    export AKS_OIDC_ISSUER="$(az aks show -n myAKSCluster -g "${RESOURCE_GROUP}" --query "oidcIssuerProfile.issuerUrl" -otsv)"

    The variable should contain the Issuer URL similar to the following example:

    https://eastus.oic.prod-aks.azure.com/00000000-0000-0000-0000-000000000000/00000000-0000-0000-0000-000000000000/
    ```
14. Create a Managed identity
    use the az identity create command to create a managed identity
    ```sh
    az identity create --name "${USER_ASSIGNED_IDENTITY_NAME}" --resource-group "${RESOURCE_GROUP}" --location "${LOCATION}" --subscription "${SUBSCRIPTION}"
    Next, let's create a variable for the managed identity ID.
    export USER_ASSIGNED_CLIENT_ID="$(az identity show --resource-group "${RESOURCE_GROUP}" --name "${USER_ASSIGNED_IDENTITY_NAME}" --query 'clientId' -otsv)"
    ```
15. Create a Kubernetes service account and annotate it with the client ID of the managed identity created in the previous step. Use the [az aks get-credentials](https://learn.microsoft.com/en-us/cli/azure/aks#az-aks-get-credentials) command and replace the values for the cluster name and the resource group name.
16. Copy and paste the following multi-line input in the Azure CLI.
    ```sh
    cat <<EOF | kubectl apply -f
    apiVersion: v1
    kind: ServiceAccount
    metadata:
       annotations:
          azure.workload.identity/client-id: "${USER_ASSIGNED_CLIENT_ID}"
       name: "${SERVICE_ACCOUNT_NAME}"
       namespace: "${SERVICE_ACCOUNT_NAMESPACE}"
    EOF

    or do a
    kubectl apply -f kubernates_fedidentity.yaml

    The following output resembles successful creation of the identity:
    Serviceaccount/workload-identity-sa created
     ```
17. Use the [az identity federated-credential create](https://learn.microsoft.com/en-us/cli/azure/identity/federated-credential#az-identity-federated-credential-create) command to create the federated identity credential between the managed identity, the service account issuer, and the subject.
```sh
az identity federated-credential create --name ${FEDERATED_IDENTITY_CREDENTIAL_NAME} --identity-name "${USER_ASSIGNED_IDENTITY_NAME}" --resource-group "${RESOURCE_GROUP}" --issuer "${AKS_OIDC_ISSUER}" --subject system:serviceaccount:"${SERVICE_ACCOUNT_NAMESPACE}":"${SERVICE_ACCOUNT_NAME}" --audience api://AzureADTokenExchange
```
18. Create federated identity between managed identity and  keda service account as well
    ```sh
    az identity federated-credential create --name ${KEDA_FEDERATED_IDENTITY_CREDENTIAL_NAME} --identity-name "${USER_ASSIGNED_IDENTITY_NAME}" --resource-group "${RESOURCE_GROUP}" --issuer "${AKS_OIDC_ISSUER}" --subject system:serviceaccount:"kube-system":"keda-operator" --audience api://AzureADTokenExchange
    ```
19. If you're using [Microsoft Entra Workload ID](https://learn.microsoft.com/en-us/azure/aks/workload-identity-overview) and you enable KEDA before Workload ID, you need to restart the KEDA operator pods so the proper environment variables can be injected:

Restart the pods by running 
```sh
kubectl rollout restart deployment keda-operator -n kube-system.

Obtain KEDA operator pods using kubectl get pod -n kube-system and finding pods that begin with keda-operator.

Verify successful injection of the environment variables by running kubectl describe pod <keda-operator-pod> -n kube-system. Under Environment, you should see values for AZURE_TENANT_ID, AZURE_FEDERATED_TOKEN_FILE, and AZURE_AUTHORITY_HOST.
```
20. Now lets deploy the function deployment file but before deploying make sure the function deployment and KEDA scaled objects are configured to connect to Service Bus queue using the workload identity
    ```sh
    Make sure workload identity is set to true and serviceAccountName under function app deployment kind are updated
    template:
    metadata:
      labels:
        app: <funcappname>
        azure.workload.identity/use: "true"
    spec:
      serviceAccountName: "${SERVICE_ACCOUNT_NAME}" or workload-identity-sa
    ```
21. Create TriggerAuthentication kind of deployment
    ```sh
    apiVersion: keda.sh/v1alpha1
    kind: TriggerAuthentication
    metadata:
      name: <trig_auth_name>
      namespace: default # must be same namespace as the ScaledObjec
    spec:
       podIdentity:
           provider:  azure-workload  # Optional. Default: none
           identityId: <USER_ASSIGNED_CLIENT_ID>
     ```
Update the scaled object to refer to the trigger auth
```sh
 authenticationRef:
      name:   <trig_auth_name>
```
Deploy the yaml file consisting of Function app, Trigger auth and scaled object deployments 

```sh
kubectl apply -f kedafuncproj_deployment.yaml
```

22. We are just one step away. You'll need to create a role assignment that provides access to your Azure Service Bus topics and queues at runtime. Assign Azure Service Bus Data Owner, Azure Service Bus Data Sender, Azure Service Bus Data Receiver role to the managed identity at the appropriate scope (Azure subscription, resource group, Service Bus namespace, or Service Bus queue or topic). For instructions to assign a role to a managed identity, see [Assign Azure roles using the Azure portal](https://learn.microsoft.com/en-us/azure/role-based-access-control/role-assignments-portal) or
   Go Azure Service Bus resource overview ->Access Control (IAM) ->Click Add and select add role assignment -> Look for Azure Service Bus Data Owner -> Click Next and Choose Managed Identity -> Select Members -> Confirm that the Subscription is the one in which you created the resources earlier ->  In the Managed identity selector, choose User-assigned managed identity category (for this sample user assigned is being used for system assigned choose the AKS cluster resource) -> Click on your user-assigned. It should move down into the Selected members section. Click Select -> Back on the Add role assignment screen, click Review + assign. Review the configuration, and then click Review + assign. Or if you wish to configure using AzCLI as some clients, such as the Azure portal, don't expose the Service Bus subscription resource as a scope for role assignment. In such cases, the Azure CLI may be used instead. To learn more, see [Azure built-in roles for Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-managed-service-identity#resource-scope). Repeat the same for Azure Service Bus Data Sender and Azure Service Bus Data Receiver role assignments.

Once the Azure Service Bus role assignments are done the Azure Service bus can be triggered and connected by KEDA Add-on scaled objects and Function apps hosted on AKS using user-assigned managed identity and workload identity from AKS side.

23. You may now send messages to the Service bus queue and you can observe that the KEDA brings up the azure function app pod and helps to scale as number of messages inrease in the queue. These connected using user-assigned managed identity and workload profiles in AKS.




   
        
    
    
      
