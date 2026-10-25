"""Queue or recover publication only for recorded, merged, routine upstream updates."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
from upstream_policy import classify


def dispatch(policy, branch):
    if not policy['routine']:
        return
    subprocess.run(['git', 'merge-base', '--is-ancestor', policy['head'], f'origin/{branch}'], check=True)
    subprocess.run(['gh', 'workflow', 'run', 'release-fork.yml', '--ref', branch,
                    '-f', 'source_commit=' + policy['head'], '-f', 'base_commit=' + policy['base'],
                    '-f', 'upstream_commit=' + policy['upstream']], check=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--policy')
    parser.add_argument('--recover', action='store_true')
    parser.add_argument('--branch', required=True)
    args = parser.parse_args()
    if args.recover:
        prs = json.loads(subprocess.check_output(['gh', 'pr', 'list', '--state', 'merged', '--head', 'codex/upstream-sync',
                         '--base', args.branch, '--limit', '1', '--json', 'author,body,headRefOid'], text=True))
        for pr in prs:
            receipt = re.search(r'<!-- routine-release:(\{[^\n]+\}) -->', pr['body'])
            if receipt and pr['author'].get('is_bot') and pr['author']['login'] == 'app/github-actions':
                recorded = json.loads(receipt[1])
                if recorded['head'] != pr['headRefOid']:
                    raise ValueError('Release recovery receipt does not match the merged PR')
                policy = classify(recorded['base'], recorded['upstream'], recorded['head'])
                dispatch(policy, args.branch)
    else:
        dispatch(json.loads(Path(args.policy).read_text()), args.branch)
