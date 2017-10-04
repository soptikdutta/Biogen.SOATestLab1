def pscmd = { String cmd ->
		"powershell -ExecutionPolicy Bypass -Command \"${cmd}\""
}
def cake = { String target ->
		"powershell -ExecutionPolicy Bypass -File build.ps1  -Target ${target} -Verbosity Diagnostic"
}

def cakeArgs = { String target, String args ->
		"powershell -ExecutionPolicy Bypass -File build.ps1  -Target ${target} -ScriptArgs '${args}'"
}


node {	
	def shouldPublishStage = (env.BRANCH_NAME == 'master')
	def shouldPublishDevelop = (env.BRANCH_NAME == 'develop')

    stage('Start') {
        echo 'Starting....'
    }
    stage('Checkout') {
        echo 'Checking out....'
		checkout scm

		echo 'deploying on {env.BRANCH_NAME}'
    }
    stage('Gather Dependencies') {
        echo 'Gathering Dependencies....'
    }
	stage('Build') {
        echo 'Building....'
		bat cake('Build')
    }
	if(shouldPublishDevelop) {
		stage('Deploy PoC') {
			echo 'Deploying....'
			bat cakeArgs('Deploy', '--Cluster="PoC"')
		}
	}
	if(shouldPublishDevelop) {
		stage('Accept') {
			echo 'Running Unit Tests....'
			bat cake('Run-Specs')
			junit 'test/Backend.Test.Spec.dll.junit.xml'
		}
	}
	if(shouldPublishDevelop) {
		stage('Deploy Dev') {
			echo 'Deploying....'
			bat cakeArgs('Deploy', '--Cluster="Cloud"')		
		}
	}
	if(shouldPublishStage) {
		stage('Deploy Stage') {
			echo 'Deploying....'
			bat cakeArgs('Deploy', '--Cluster="Stage"')		
		}
	}
	if(shouldPublishStage) {
		stage('Generate SDK') {
			echo 'Deploying....'
			bat cake('Generate-Sdk')		
		}
	}

}