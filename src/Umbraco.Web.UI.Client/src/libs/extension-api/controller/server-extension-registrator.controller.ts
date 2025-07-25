import type { ManifestBase } from '../types/index.js';
import { isManifestBaseType } from '../type-guards/index.js';
import { ManifestService, type ManifestResponseModel } from '@umbraco-cms/backoffice/external/backend-api';
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type { UmbBackofficeExtensionRegistry } from '@umbraco-cms/backoffice/extension-registry';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { UMB_SERVER_CONTEXT } from '@umbraco-cms/backoffice/server';

// TODO: consider if this can be replaced by the new extension controllers
export class UmbServerExtensionRegistrator extends UmbControllerBase {
	#extensionRegistry: UmbBackofficeExtensionRegistry;

	constructor(host: UmbControllerHost, extensionRegistry: UmbBackofficeExtensionRegistry) {
		super(host, UmbServerExtensionRegistrator.name);
		this.#extensionRegistry = extensionRegistry;
	}

	/**
	 * Registers all extensions from the server.
	 * This is used to register all extensions that are available to the user (including private extensions).
	 * @remark Users must have the BACKOFFICE_ACCESS permission to access this method.
	 */
	public async registerAllExtensions() {
		const { data: packages } = await tryExecute(this, ManifestService.getManifestManifest());
		if (packages) {
			await this.#loadServerPackages(packages);
		}
	}

	/**
	 * Registers all private extensions from the server.
	 * This is used to register all private extensions that are available to the user.
	 * @remark Users must have the BACKOFFICE_ACCESS permission to access this method.
	 */
	public async registerPrivateExtensions() {
		const { data: packages } = await tryExecute(this, ManifestService.getManifestManifestPrivate(), {
			disableNotifications: true,
		});
		if (packages) {
			await this.#loadServerPackages(packages);
		}
	}

	/**
	 * Registers all public extensions from the server.
	 * This is used to register all extensions that are available to the user (excluding private extensions) such as login extensions.
	 * @remark Any user can access this method without any permissions.
	 */
	public async registerPublicExtensions() {
		const { data: packages } = await tryExecute(this, ManifestService.getManifestManifestPublic(), {
			disableNotifications: true,
		});
		if (packages) {
			await this.#loadServerPackages(packages);
		}
	}

	async #loadServerPackages(packages: ManifestResponseModel[]) {
		const extensions: ManifestBase[] = [];

		const serverContext = await this.getContext(UMB_SERVER_CONTEXT);

		if (!serverContext) {
			throw new Error('Server context is not available');
		}

		const apiBaseUrl = serverContext?.getServerUrl();

		packages?.forEach((p) => {
			p.extensions?.forEach((e) => {
				// Crudely validate that the extension at least follows a basic manifest structure
				// Idea: Use `Zod` to validate the manifest
				if (isManifestBaseType(e)) {
					/**
					 * Crude check to see if extension is of type "js" since it is safe to assume we do not
					 * need to load any other types of extensions in the backoffice (we need a js file to load)
					 */

					// TODO: add helper to check for relative paths
					// Add base url if the js path is relative
					if ('js' in e && typeof e.js === 'string' && !e.js.startsWith('http')) {
						e.js = `${apiBaseUrl}${e.js}`;
					}

					// Add base url if the element path is relative
					if ('element' in e && typeof e.element === 'string' && !e.element.startsWith('http')) {
						e.element = `${apiBaseUrl}${e.element}`;
					}

					// Add base url if the element path api relative
					if ('api' in e && typeof e.api === 'string' && !e.api.startsWith('http')) {
						e.api = `${apiBaseUrl}${e.api}`;
					}

					extensions.push(e);
				}
			});
		});

		this.#extensionRegistry.registerMany(extensions);
	}
}
