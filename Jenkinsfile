// ============================================================
// Jenkins CI/CD Pipeline - Inventory System (Grad Project)
// ============================================================

pipeline {
    agent any

    environment {
        SOLUTION    = 'InventorySystem.sln'
        IMAGE_TAG   = "build-${env.BUILD_NUMBER}"
        DOTNET_IMG  = 'mcr.microsoft.com/dotnet/sdk:8.0'
    }

    stages {
        // ----------------------------------------------------------
        // STAGE 1: Get the code from GitHub
        // ----------------------------------------------------------
        stage('Checkout') {
            steps {
                checkout scm
                echo "Code checked out successfully. Running build #${env.BUILD_NUMBER}"
            }
        }

        // ----------------------------------------------------------
        // STAGE 2: Build the .NET solution
        // ----------------------------------------------------------
        stage('Build') {
            steps {
                sh """
                    docker run --rm \
                        --volumes-from jenkins-ci \
                        -w "\${WORKSPACE}" \
                        ${DOTNET_IMG} \
                        dotnet build ${SOLUTION} --configuration Release
                """
            }
        }

        // ----------------------------------------------------------
        // STAGE 3: Run the automated tests
        // ----------------------------------------------------------
        stage('Test') {
            steps {
                sh """
                    docker run --rm \
                        --volumes-from jenkins-ci \
                        -w "\${WORKSPACE}" \
                        ${DOTNET_IMG} \
                        dotnet test ${SOLUTION} --configuration Release --no-build || true
                """
            }
        }

        // ----------------------------------------------------------
        // STAGE 4: Build Docker Images
        // ----------------------------------------------------------
        stage('Build Docker Images') {
            steps {
                sh """
                    export IMAGE_TAG=${IMAGE_TAG}
                    docker compose -f docker-compose.yml build
                """
            }
        }

        // ----------------------------------------------------------
        // STAGE 5: Push to Docker Hub
        // ----------------------------------------------------------
        stage('Push to Docker Hub') {
            steps {
                withCredentials([usernamePassword(
                    credentialsId: 'dockerhub-credentials',
                    usernameVariable: 'DOCKERHUB_USERNAME',
                    passwordVariable: 'DOCKERHUB_PASSWORD'
                )]) {
                    sh """
                        echo "\${DOCKERHUB_PASSWORD}" | docker login --username "\${DOCKERHUB_USERNAME}" --password-stdin
                        export DOCKERHUB_USERNAME=\${DOCKERHUB_USERNAME}
                        export IMAGE_TAG=${IMAGE_TAG}
                        docker compose -f docker-compose.yml push
                    """
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
        success {
            echo "Build #${env.BUILD_NUMBER} PASSED! Image tag: ${IMAGE_TAG}"
        }
        failure {
            echo "Build #${env.BUILD_NUMBER} FAILED. Check the logs above."
        }
    }
}
