pipeline {
    agent any

    options {
        timestamps()
        timeout(time: 45, unit: 'MINUTES')
        disableConcurrentBuilds()
        buildDiscarder(logRotator(numToKeepStr: '20'))
    }

    // Start a build when GitHub sends a push webhook (GitHub plugin; webhook URL: <jenkins>/github-webhook/).
    triggers {
        githubPush()
    }

    parameters {
        booleanParam(name: 'PUSH_IMAGES', defaultValue: false,
                     description: 'Push images to Docker Hub even when not building main.')
    }

    environment {
        SOLUTION                    = 'InventorySystem.sln'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO               = '1'
        DOTNET_CLI_HOME             = "${WORKSPACE}/.dotnet"
        NUGET_PACKAGES              = "${WORKSPACE}/.nuget/packages"
        DOCKERHUB_CREDENTIALS_ID    = 'dockerhub-credentials'
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
            steps {
                withCredentials([usernamePassword(credentialsId: env.DOCKERHUB_CREDENTIALS_ID,
                                                  usernameVariable: 'DOCKERHUB_USERNAME',
                                                  passwordVariable: 'DOCKERHUB_PASSWORD')]) {
                    sh '''
                        echo "$DOCKERHUB_PASSWORD" | docker login -u "$DOCKERHUB_USERNAME" --password-stdin
                        docker compose -f docker-compose.yml push \
                            product-api identity-api inventory-api alert-api reporting-api console

                        # Also publish the build as :latest.
                        for repo in inventory-identity-api inventory-inventory-api inventory-alert-api \
                                    inventory-reporting-api inventory-console; do
                            docker tag  "$DOCKERHUB_USERNAME/$repo:$IMAGE_TAG" "$DOCKERHUB_USERNAME/$repo:latest"
                            docker push "$DOCKERHUB_USERNAME/$repo:latest"
                        done
                    '''
                }
            }
            post {
                always {
                    sh 'docker logout || true'
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
