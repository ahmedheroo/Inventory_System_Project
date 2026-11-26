// CI/CD for InventorySystem. Runs on the Jenkins installed on the dev EC2 (infra/ansible, role `jenkins`).
//
//   build + test -> docker build -> push to Docker Hub -> deploy dev -> approve -> deploy prod
//
// Jenkins credentials used (Manage Jenkins -> Credentials):
//   dockerhub-credentials  Username with password  Docker Hub user + access token
//   ansible-vault-pass     Secret file             the password of group_vars/*/vault.yml
//   prod-ssh-key           SSH username with key   user `ubuntu` + the EC2 key pair's .pem
//
// Pushing images and deploying only happen on `main`; other branches stop after Docker Build.

pipeline {
    agent any

    options {
        timestamps()
        disableConcurrentBuilds()
        buildDiscarder(logRotator(numToKeepStr: '20'))
    }

    // Start a build when GitHub sends a push webhook (GitHub plugin; webhook URL: <jenkins>/github-webhook/).
    triggers {
        githubPush()
    }

    parameters {
        booleanParam(name: 'PUSH_IMAGES', defaultValue: false,
                     description: 'Push images to Docker Hub even when not building main (no deploy).')
    }

    environment {
        SOLUTION                    = 'InventorySystem.sln'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO               = '1'
        DOTNET_CLI_HOME             = "${WORKSPACE}/.dotnet"
        NUGET_PACKAGES              = "${WORKSPACE}/.nuget/packages"
        DOCKERHUB_CREDENTIALS_ID    = 'dockerhub-credentials'
        ANSIBLE_FORCE_COLOR         = 'true'
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
                script {
                    env.GIT_SHORT_SHA = sh(script: 'git rev-parse --short HEAD', returnStdout: true).trim()
                    env.IMAGE_TAG     = "${env.BUILD_NUMBER}-${env.GIT_SHORT_SHA}"
                }
                echo "Image tag: ${env.IMAGE_TAG}"
            }
        }

        stage('Build & Test') {
            options {
                timeout(time: 30, unit: 'MINUTES')
            }
            agent {
                docker {
                    image 'mcr.microsoft.com/dotnet/sdk:8.0'
                    reuseNode true
                }
            }
            stages {
                stage('Restore') {
                    steps {
                        sh 'dotnet restore "$SOLUTION"'
                    }
                }
                stage('Build') {
                    steps {
                        sh 'dotnet build "$SOLUTION" --configuration Release --no-restore'
                    }
                }
                stage('Test') {
                    steps {
                        sh '''
                            dotnet test "$SOLUTION" \
                                --configuration Release \
                                --no-build \
                                --logger "trx;LogFilePrefix=results" \
                                --results-directory TestResults
                        '''
                    }
                    post {
                        always {
                            archiveArtifacts allowEmptyArchive: true, artifacts: 'TestResults/**/*.trx'
                        }
                    }
                }
            }
        }

        stage('Docker Build') {
            options {
                timeout(time: 30, unit: 'MINUTES')
            }
            steps {
                withCredentials([usernamePassword(credentialsId: env.DOCKERHUB_CREDENTIALS_ID,
                                                  usernameVariable: 'DOCKERHUB_USERNAME',
                                                  passwordVariable: 'DOCKERHUB_PASSWORD')]) {
                    sh 'docker compose -f docker-compose.yml build --pull'
                }
            }
        }

        stage('Docker Push') {
            when {
                anyOf {
                    branch 'main'
                    expression { return params.PUSH_IMAGES }
                }
            }
            options {
                timeout(time: 20, unit: 'MINUTES')
            }
            steps {
                withCredentials([usernamePassword(credentialsId: env.DOCKERHUB_CREDENTIALS_ID,
                                                  usernameVariable: 'DOCKERHUB_USERNAME',
                                                  passwordVariable: 'DOCKERHUB_PASSWORD')]) {
                    sh '''
                        echo "$DOCKERHUB_PASSWORD" | docker login -u "$DOCKERHUB_USERNAME" --password-stdin
                        docker compose -f docker-compose.yml push \
                            product-api identity-api inventory-api alert-api reporting-api console

                        # Also publish the build as :latest.
                        for repo in inventory-product-api inventory-identity-api inventory-inventory-api \
                                    inventory-alert-api inventory-reporting-api inventory-console; do
                            docker tag  "$DOCKERHUB_USERNAME/$repo:$IMAGE_TAG" "$DOCKERHUB_USERNAME/$repo:latest"
                            docker push "$DOCKERHUB_USERNAME/$repo:latest"
                        done
                    '''
                }
            }
            post {
                always {
                    sh 'docker logout || true'
                    // The dev EC2 has a 30 GB disk: drop old build layers and untagged images.
                    sh 'docker image prune -f --filter "until=72h" || true'
                    sh 'docker builder prune -f --filter "until=72h" || true'
                }
            }
        }

        // Jenkins runs on the dev EC2, so dev is deployed through a local connection as the jenkins user
        // (the `k3s` role gave it a kubeconfig).
        stage('Deploy Dev') {
            when {
                branch 'main'
            }
            options {
                timeout(time: 20, unit: 'MINUTES')
            }
            steps {
                withCredentials([file(credentialsId: 'ansible-vault-pass', variable: 'VAULT_PASS_FILE')]) {
                    dir('infra/ansible') {
                        sh '''
                            ansible-playbook deploy.yml \
                                --limit dev \
                                --connection local \
                                --vault-password-file "$VAULT_PASS_FILE" \
                                -e image_tag="$IMAGE_TAG"
                        '''
                    }
                }
            }
        }

        stage('Approve Prod') {
            when {
                branch 'main'
            }
            options {
                timeout(time: 1, unit: 'DAYS')
            }
            steps {
                input message: "Deploy ${env.IMAGE_TAG} to production?", ok: 'Deploy to prod'
            }
        }

        // Prod is reached over SSH on its private IP: the prod security group only lets the dev EC2's
        // security group in, and such rules only match traffic over private addresses.
        stage('Deploy Prod') {
            when {
                branch 'main'
            }
            options {
                timeout(time: 20, unit: 'MINUTES')
            }
            steps {
                withCredentials([
                    file(credentialsId: 'ansible-vault-pass', variable: 'VAULT_PASS_FILE'),
                    sshUserPrivateKey(credentialsId: 'prod-ssh-key', keyFileVariable: 'SSH_KEY',
                                      usernameVariable: 'SSH_USER')
                ]) {
                    dir('infra/ansible') {
                        sh '''
                            ansible-playbook deploy.yml \
                                --limit prod \
                                --user "$SSH_USER" \
                                --private-key "$SSH_KEY" \
                                --vault-password-file "$VAULT_PASS_FILE" \
                                -e 'ansible_host={{ private_ip }}' \
                                -e image_tag="$IMAGE_TAG"
                        '''
                    }
                }
            }
        }
    }

    post {
        always {
            cleanWs(deleteDirs: true, notFailBuild: true)
        }
        success {
            echo "Build ${env.IMAGE_TAG} succeeded."
        }
        failure {
            echo "Build ${env.IMAGE_TAG} failed."
        }
    }
}
