pipeline {
	agent any
	stages {
		stage('nugget restore'){
			steps{
				 echo "nugget restoring packages"
		         	bat 'C:/tools/nuget.exe  restore  src/SmartStoreNet.sln'
				
					}
				}
				stage('Build') {
    					steps {
                                        echo " building "
						
						bat "\"${tool 'MSBuild'}\" src/SmartStoreNet.sln /p:DeployOnBuild=true /p:DeployDefaultTarget=WebPublish /p:WebPublishMethod=FileSystem /p:SkipInvalidConfigurations=true /t:build /p:Configuration=Release /p:Platform=\"Any CPU\" /p:publishUrl=c:\\dott"

					}
				}
			}
}

