/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  output: "standalone", // slim, self-contained server image for docker deploy
};
export default nextConfig;
