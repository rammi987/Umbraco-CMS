import type { UmbWorkspaceEntityIsNewConditionConfig } from '../types.js';
import { UMB_SUBMITTABLE_WORKSPACE_CONTEXT } from '../../contexts/index.js';
import { UmbConditionBase } from '@umbraco-cms/backoffice/extension-registry';
import type { UmbConditionControllerArguments, UmbExtensionCondition } from '@umbraco-cms/backoffice/extension-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';

const ObserveSymbol = Symbol();

export class UmbWorkspaceEntityIsNewCondition
	extends UmbConditionBase<UmbWorkspaceEntityIsNewConditionConfig>
	implements UmbExtensionCondition
{
	constructor(host: UmbControllerHost, args: UmbConditionControllerArguments<UmbWorkspaceEntityIsNewConditionConfig>) {
		super(host, args);

		this.consumeContext(UMB_SUBMITTABLE_WORKSPACE_CONTEXT, (context) => {
			this.observe(
				context?.isNew,
				(isNew) => {
					if (isNew !== undefined) {
						// Check if equal to match, if match not set it defaults to true.
						this.permitted = isNew === (this.config.match !== undefined ? this.config.match : true);
					}
				},
				ObserveSymbol,
			);
		});
	}
}
