#git for-each-ref --sort=committerdate refs/heads/ --format='%(HEAD) %(color:yellow)%(refname:short)%(color:reset) - %(color:red)%(objectname:short)%(color:reset) - %(contents:subject) - %(authorname) (%(color:green)%(committerdate:relative)%(color:reset))'

git for-each-ref --sort=committerdate refs/heads/ --format=' %(HEAD) %(color:yellow)%(refname:short) (%(color:green)%(committerdate:relative)%(color:reset))'

pause