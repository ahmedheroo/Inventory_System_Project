# Deployment: AWS EC2, Ansible, Jenkins, Docker and k3s

Two environments, each a single EC2 instance running its own one-node Kubernetes cluster (k3s):

| | dev EC2 | prod EC2 |
| --- | --- | --- |
| Runs | k3s + the dev stack, **and Jenkins** | k3s + the prod stack |
| `ASPNETCORE_ENVIRONMENT` | Development (sample catalogue, Swagger) | Production |
| Deployed | automatically, on every push to `main` | after a manual approval in Jenkins |
| URL | `http://<dev-elastic-ip>/` | `http://<prod-elastic-ip>/` |

```
GitHub push ──webhook──▶ Jenkins (dev EC2)
                          ├─ dotnet build + test        (in the .NET 8 SDK container)
                          ├─ docker compose build/push  ──▶ Docker Hub  <user>/inventory-*:<build>-<sha>
                          ├─ ansible deploy.yml -l dev  ──▶ k3s on dev   (local)
                          ├─ input: approve prod
                          └─ ansible deploy.yml -l prod ──▶ k3s on prod  (SSH, private IP)
```

| Path | What it is |
| --- | --- |
| [`k8s/base`](../k8s/base) | Kubernetes manifests: SQL Server + RabbitMQ StatefulSets, the six apps, Traefik ingress for the console |
| [`k8s/overlays/{dev,prod}`](../k8s/overlays) | Per-environment settings (Kustomize) |
| [`infra/ansible`](../infra/ansible) | `site.yml` configures the servers, `deploy.yml` deploys a release |
| [`Jenkinsfile`](../Jenkinsfile) | The pipeline |

> **Memory.** A t3.medium has 4 GB. SQL Server is capped at 2 GB (`MSSQL_MEMORY_LIMIT_MB`), each app at
> 256 MiB, Jenkins at 512 MB of heap, and Ansible adds 4 GB of swap. Dev is the tight one (Jenkins and a
> `dotnet build` run next to the stack). If builds get OOM-killed, resize only the dev instance to
> t3.large (stop → *Change instance type* → start; the Elastic IP stays).

## 1. Create the EC2 instances (AWS console, once)

1. **Key pair**: EC2 → Key pairs → *Create* (`inventory-key`, `.pem`). Keep the file; Ansible and Jenkins use it.
2. **Security groups** (EC2 → Security groups), in the default VPC:
   - `inventory-dev`
     - SSH 22 from *My IP*
     - TCP 8080 (Jenkins) from *My IP*, and from GitHub's hook addresses
       (`https://api.github.com/meta`, the `hooks` list: currently `192.30.252.0/22`, `185.199.108.0/22`,
       `140.82.112.0/20`, `143.55.64.0/20`)
     - HTTP 80 from `0.0.0.0/0`
   - `inventory-prod`
     - SSH 22 from *My IP*, and from the security group `inventory-dev` (so Jenkins can deploy)
     - HTTP 80 from `0.0.0.0/0`
   - Never open 6443 (Kubernetes API), 1433, 5672 or 15672.
3. **Instances** (EC2 → Launch instance), twice:
   - Name `inventory-dev` / `inventory-prod`
   - AMI **Ubuntu Server 24.04 LTS** (x86_64), type **t3.medium**
   - Key pair `inventory-key`, security group `inventory-dev` / `inventory-prod`
   - Storage **30 GiB gp3**
4. **Elastic IPs**: EC2 → Elastic IPs → *Allocate*, twice, then *Associate* one with each instance, so the
   addresses survive a stop/start.
5. Write the addresses into [`infra/ansible/inventory/hosts.ini`](../infra/ansible/inventory/hosts.ini):
   both Elastic IPs, and the prod instance's **private** IPv4 (instance details) as `private_ip`.

## 2. Ansible on your PC (WSL)

Ansible does not run on Windows itself; use the Ubuntu WSL distribution:

```bash
sudo apt update && sudo apt install -y ansible
```

Copy the key somewhere with Linux permissions (SSH refuses a key on `/mnt/c` that is readable by others):

```bash
mkdir -p ~/.ssh && cp /mnt/c/Users/<you>/Downloads/inventory-key.pem ~/.ssh/ && chmod 600 ~/.ssh/inventory-key.pem
```

Run everything below from `infra/ansible` (inside WSL: `cd /mnt/e/WORK/InventorySystem/infra/ansible`).
Ansible ignores an `ansible.cfg` in a world-writable folder, which `/mnt/e` is under WSL's default
mount options. Either export it explicitly:

```bash
export ANSIBLE_CONFIG=$PWD/ansible.cfg
```

or clone the repository inside the WSL home directory and work there.

### 2.1 Secrets (ansible-vault)

For each environment, copy the example, fill in real values, and encrypt it:

```bash
cp group_vars/dev/vault.example.yml  group_vars/dev/vault.yml
cp group_vars/prod/vault.example.yml group_vars/prod/vault.yml
# edit both (different values for prod!), then:
ansible-vault encrypt group_vars/dev/vault.yml group_vars/prod/vault.yml
```

Use **one** vault password for both files; it becomes the Jenkins credential `ansible-vault-pass`.
The encrypted `vault.yml` files are committed (Jenkins reads them from the checkout); never commit them
unencrypted. Edit later with `ansible-vault edit group_vars/prod/vault.yml`.

Set your Docker Hub user in [`group_vars/all/main.yml`](../infra/ansible/group_vars/all/main.yml)
(`dockerhub_username`). The image repositories must be **public**, or the cluster can't pull them.

### 2.2 Configure both servers

```bash
ansible all -m ping --private-key ~/.ssh/inventory-key.pem
ansible-playbook site.yml --private-key ~/.ssh/inventory-key.pem
```

This updates the OS, adds swap, installs k3s on both, and Docker + Jenkins (+ Ansible) on dev. The last
task prints Jenkins' URL and initial admin password. Check:

```bash
ssh -i ~/.ssh/inventory-key.pem ubuntu@<dev-ip> 'kubectl get nodes; sudo -u jenkins docker info --format "{{.ServerVersion}}"'
ssh -i ~/.ssh/inventory-key.pem ubuntu@<prod-ip> 'kubectl get nodes'
```

`site.yml` is safe to run again at any time.

## 3. Jenkins (once)

1. Open `http://<dev-elastic-ip>:8080`, paste the initial admin password, install the suggested plugins,
   create your admin user.
2. *Manage Jenkins → Plugins*, install: **Docker Pipeline**, **GitHub**, **Workspace Cleanup**,
   **Pipeline: Stage View** (Timestamper and Credentials Binding come with the suggested set).
3. *Manage Jenkins → Credentials → (global) → Add*:

   | ID | Kind | Value |
   | --- | --- | --- |
   | `dockerhub-credentials` | Username with password | Docker Hub user + an access token |
   | `ansible-vault-pass` | Secret file | a text file holding the vault password |
   | `prod-ssh-key` | SSH Username with private key | username `ubuntu`, the contents of `inventory-key.pem` |

4. *New Item* → **Pipeline** (name `InventorySystem`):
   - *Triggers*: **GitHub hook trigger for GITScm polling**
   - *Pipeline*: *Pipeline script from SCM* → Git → `https://github.com/ahmedheroo/Inventory_System_Project.git`,
     branch `*/main`, script path `Jenkinsfile`.
5. On GitHub: repository → *Settings → Webhooks → Add webhook*:
   payload URL `http://<dev-elastic-ip>:8080/github-webhook/` (trailing slash), content type
   `application/json`, *Just the push event*.

## 4. First deployment

The first run of the pipeline needs images in Docker Hub, so start it by hand: *Build Now* in Jenkins
(or push to `main`). It builds, pushes `<build>-<sha>`, deploys dev, then waits on **Approve Prod**.

To deploy a tag by hand from WSL instead:

```bash
ansible-playbook deploy.yml -l dev  -e image_tag=<tag> --ask-vault-pass --private-key ~/.ssh/inventory-key.pem
ansible-playbook deploy.yml -l prod -e image_tag=<tag> --ask-vault-pass --private-key ~/.ssh/inventory-key.pem
```

The playbook creates the `inventory` namespace and the `inventory-secrets` Secret, applies
`k8s/overlays/<env>` with every image pinned to the tag, waits for every rollout, and checks that
`/sign-in` answers. Sign in with `manager`, `clerk` or `viewer` and the environment's
`vault_seed_user_password`.

**Rollback** = deploy an older tag: re-run *Deploy Prod* of an older Jenkins build (*Replay*), or
`deploy.yml -e image_tag=<older tag>`.

## 5. Day-to-day

```bash
kubectl -n inventory get pods                       # on either server, as ubuntu
kubectl -n inventory logs deploy/inventory-api -f
kubectl -n inventory rollout restart deploy/console
kubectl top pods -n inventory; free -h              # memory
```

- SQL Server and RabbitMQ keep their data on `local-path` volumes on the instance disk
  (`/var/lib/rancher/k3s/storage`). Take EBS snapshots of the prod volume for backups.
- The SQL and RabbitMQ passwords are applied only when their volume is first created; changing them in the
  vault later does not change the stored ones (same caveat as with docker compose).
- Everything is plain HTTP. For a real domain, point DNS at the prod Elastic IP and add TLS on Traefik
  (e.g. cert-manager with Let's Encrypt).
