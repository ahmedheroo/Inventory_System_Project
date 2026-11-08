pipeline {
    agent any

    options {
        timestamps()
        timeout(time: 60, unit: 'MINUTES')
        disableConcurrentBuilds()
        buildDiscarder(logRotator(numToKeepStr: '20'))
    }

    triggers {
        githubPush()
    }

    environment {
        SOLUTION                    = 'InventorySystem.sln'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO               = '1'
        DOCKERHUB_CREDENTIALS_ID    = 'dockerhub-credentials'
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
                script {
                    env.GIT_SHORT_SHA = sh(
                        script: 'git rev-parse --short=8 HEAD',
                        returnStdout: true
                    ).trim()
                    env.IMAGE_TAG = "${env.BUILD_NUMBER}-${env.GIT_SHORT_SHA}"
                    echo "Building commit ${env.GIT_SHORT_SHA}; Docker tag: ${env.IMAGE_TAG}"
                }
            }
        }

        stage('Verify tools') {
            steps {
                sh '''
                    set -eu
                    dotnet --info
                    docker --version
                    docker compose version
                    docker info >/dev/null
                '''
            }
        }

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
                    archiveArtifacts(
                        artifacts: 'TestResults/**/*.trx',
                        allowEmptyArchive: true
                    )
                }
            }
        }

        stage('Build Docker images') {
            steps {
                withCredentials([usernamePassword(
                    credentialsId: env.DOCKERHUB_CREDENTIALS_ID,
                    usernameVariable: 'DOCKERHUB_USERNAME',
                    passwordVariable: 'DOCKERHUB_PASSWORD'
                )]) {
                    sh '''
                        set -eu
                        export IMAGE_TAG
                        docker compose -f docker-compose.yml config --quiet
                        docker compose -f docker-compose.yml build --pull \
                            product-api identity-api inventory-api \
                            alert-api reporting-api console
                    '''
                }
            }
        }

        stage('Push Docker images') {
            // This Jenkins job should be configured to build the main branch.
            when {
                anyOf {
                    branch 'main'
                    expression { return env.GIT_BRANCH == 'origin/main' }
                }
            }
            steps {
                withCredentials([usernamePassword(
                    credentialsId: env.DOCKERHUB_CREDENTIALS_ID,
                    usernameVariable: 'DOCKERHUB_USERNAME',
                    passwordVariable: 'DOCKERHUB_PASSWORD'
                )]) {
                    sh '''
                        set -eu
                        echo "$DOCKERHUB_PASSWORD" | docker login \
                            --username "$DOCKERHUB_USERNAME" \
                            --password-stdin

                        # Push immutable build-specific tags first.
                        docker compose -f docker-compose.yml push \
                            product-api identity-api inventory-api \
                            alert-api reporting-api console

                        # Publish latest for all six application images.
                        for repo in \
                            inventory-product-api \
                            inventory-identity-api \
                            inventory-inventory-api \
                            inventory-alert-api \
                            inventory-reporting-api \
                            inventory-console
                        do
                            docker tag \
                                "$DOCKERHUB_USERNAME/$repo:$IMAGE_TAG" \
                                "$DOCKERHUB_USERNAME/$repo:latest"
                            docker push "$DOCKERHUB_USERNAME/$repo:latest"
                        done
                    '''
                }
            }
            post {
                always {
                    sh 'docker logout >/dev/null 2>&1 || true'
                }
            }
        }
    }

    post {
        success {
            echo "CI pipeline completed successfully. Image tag: ${env.IMAGE_TAG}"
        }
        failure {
            echo 'CI pipeline failed. Check the stage logs before publishing or deploying.'
        }
    }
}
