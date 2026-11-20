// ============================================================
// Jenkins CI/CD Pipeline - Inventory System (Grad Project)
// ============================================================
// What this file does, in plain English:
//   1. Pulls the latest code from GitHub
//   2. Builds the .NET solution inside a Docker container
//   3. Runs the tests
//   4. Builds Docker images for all 6 microservices
//   5. Pushes the images to Docker Hub
// ============================================================

pipeline {

    // "agent any" means: run on whatever machine Jenkins has available
    agent any

    // Global variables shared across all stages
    environment {
        SOLUTION    = 'InventorySystem.sln'           // the .NET solution file
        IMAGE_TAG   = "build-${env.BUILD_NUMBER}"     // e.g. "build-5" — unique per run
        DOTNET_IMG  = 'mcr.microsoft.com/dotnet/sdk:8.0' // official .NET 8 container
    }

    stages {

        // ----------------------------------------------------------
        // STAGE 1: Get the code from GitHub
        // ----------------------------------------------------------
        stage('Checkout') {
            steps {
                // Jenkins automatically clones the repo linked to this job
                checkout scm
                echo "Code checked out. Running build #${env.BUILD_NUMBER}"
            }
        }

        // ----------------------------------------------------------
        // STAGE 2: Build the .NET solution
        //   We use a Docker container that already has .NET 8 SDK installed.
        //   This avoids having to install .NET directly on the Jenkins server.
        // ----------------------------------------------------------
        stage('Build') {
            steps {
                // -v mounts the current workspace folder into the container at /src
                sh """
                    docker run --rm \
                        -v "\${WORKSPACE}:/src" \
                        -w /src \
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
                        -v "\${WORKSPACE}:/src" \
                        -w /src \
                        ${DOTNET_IMG} \
                        dotnet test ${SOLUTION} --configuration Release --no-build
                """
            }
            post {
                // Always save the test result files, even if tests fail
                always {
                    archiveArtifacts artifacts: 'TestResults/**/*.trx', allowEmptyArchive: true
                }
            }
        }

        // ----------------------------------------------------------
        // STAGE 4: Build all 6 microservice Docker images
        //   docker-compose.yml knows how to build each one using the Dockerfile
        // ----------------------------------------------------------
        stage('Build Docker Images') {
            steps {
                // withCredentials safely injects our Docker Hub username/password
                // without printing them in the logs
                withCredentials([usernamePassword(
                    credentialsId: 'dockerhub-credentials',
                    usernameVariable: 'DOCKERHUB_USERNAME',
                    passwordVariable: 'DOCKERHUB_PASSWORD'
                )]) {
                    sh """
                        export DOCKERHUB_USERNAME=\${DOCKERHUB_USERNAME}
                        export IMAGE_TAG=${IMAGE_TAG}
                        docker compose -f docker-compose.yml build
                    """
                }
            }
        }

        // ----------------------------------------------------------
        // STAGE 5: Push images to Docker Hub
        //   Only runs when we are on the 'main' branch
        // ----------------------------------------------------------
        stage('Push to Docker Hub') {
            when {
                // Only push on the main branch — not on feature branches
                branch 'main'
            }
            steps {
                withCredentials([usernamePassword(
                    credentialsId: 'dockerhub-credentials',
                    usernameVariable: 'DOCKERHUB_USERNAME',
                    passwordVariable: 'DOCKERHUB_PASSWORD'
                )]) {
                    sh """
                        # Login to Docker Hub
                        echo "\${DOCKERHUB_PASSWORD}" | docker login --username "\${DOCKERHUB_USERNAME}" --password-stdin

                        # Push all 6 service images
                        export DOCKERHUB_USERNAME=\${DOCKERHUB_USERNAME}
                        export IMAGE_TAG=${IMAGE_TAG}
                        docker compose -f docker-compose.yml push
                    """
                }
            }
            post {
                // Always logout after pushing — good security habit
                always {
                    sh 'docker logout || true'
                }
            }
        }
    }

    // ----------------------------------------------------------
    // After every build (success or failure), print a summary
    // ----------------------------------------------------------
    post {
        success {
            echo "Build #${env.BUILD_NUMBER} PASSED! Image tag: ${IMAGE_TAG}"
        }
        failure {
            echo "Build #${env.BUILD_NUMBER} FAILED. Check the red stage above for details."
        }
    }
}
